using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormActionRunRecorderTests
{
    [Fact]
    public async Task An_empty_role_is_sealed_without_a_preparation_failure()
    {
        var fixture=new FormActionFixture();var role=FormActionFixture.Action("NotifyRole") with{RoleId=fixture.Rules.RoleId};
        var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[role])]);fixture.Recipients.People.Clear();
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var snapshot=FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        Assert.Null(snapshot.PreparationFailureCode);Assert.Empty(snapshot.Recipients);
    }
    [Fact]
    public async Task Role_recipient_names_are_fixed_with_the_accepted_recipients()
    {
        var fixture=new FormActionFixture();var role=FormActionFixture.Action("NotifyRole") with{RoleId=fixture.Rules.RoleId};
        var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[role])]);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        fixture.Recipients.People[captured.Record.ActorUserId]=new(captured.Record.ActorUserId,"en","Changed later");
        var snapshot=FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        Assert.Equal("Author",Assert.Single(snapshot.Recipients).DisplayName);
    }
    [Theory]
    [InlineData(false,4097,"es")]
    [InlineData(false,4097,"en")]
    [InlineData(true,1025,"es")]
    [InlineData(true,1025,"en")]
    public async Task Expanded_messages_are_rejected_before_effects_instead_of_being_truncated(bool includePdf,int length,string culture)
    {
        var fixture=new FormActionFixture();var message=FormActionFixture.Action(includePdf:includePdf) with{Text=new Dictionary<string,string>{{"es","{Visita}"},{"en","{Visit}"}}};
        var actions=includePdf?new[]{FormActionFixture.Action("GeneratePdf"),message}:new[]{message};var eventName=includePdf?"Approved":"Received";
        var captured=fixture.Capture([new(Guid.NewGuid(),eventName,[],actions)],new string('a',length));
        fixture.Recipients.People[captured.Record.ActorUserId]=new(captured.Record.ActorUserId,culture);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,eventName,CancellationToken.None);
        var snapshot=FormDraftPolicy.Read<FormActionSnapshot>(fixture.Runs.Runs[^1].SnapshotJson);
        Assert.Equal("Forms.Action.MessageTooLong",snapshot.PreparationFailureCode);
    }
    [Fact]
    public async Task Replaying_an_event_preserves_its_revision_ids_and_dependencies_without_duplicate_runs()
    {
        var fixture=new FormActionFixture();var first=FormActionFixture.Action();var second=FormActionFixture.Action();
        var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[first,second]),new(Guid.NewGuid(),"Received",[new(fixture.Rules.FieldId,"contains",["absent"])],[FormActionFixture.Action()])]);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var ids=fixture.Runs.Runs.Select(run=>run.Id).ToArray();
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        Assert.Equal(ids,fixture.Runs.Runs.Select(run=>run.Id));Assert.Equal(3,ids.Length);
        Assert.Equal(ids[0],fixture.Runs.Runs[1].DependsOnRunId);Assert.Null(fixture.Runs.Runs[2].DependsOnRunId);
        Assert.Equal(FormActionRunState.NotApplicable,fixture.Runs.Runs[2].State);
        Assert.All(fixture.Runs.Runs,run=>Assert.Equal(captured.Record.RuleSetId,run.RuleSetId));
    }
    [Fact]
    public async Task Question_tokens_keep_the_saved_field_identity_after_a_historical_label_changes()
    {
        var fixture=new FormActionFixture();var action=FormActionFixture.Action();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[action])]);
        var historical=fixture.Schema with{Questions=[fixture.Schema.Questions[0] with{Label=new Dictionary<string,string>{{"es","Anterior"},{"en","Previous"}}}]};
        var snapshot=captured.Snapshot with{Response=captured.Snapshot.Response with{Record=captured.Snapshot.Response.Record with{SchemaJson=FormDraftPolicy.Serialize(historical)}}};
        await fixture.Recorder.RecordAsync(captured.Record,snapshot,"Received",CancellationToken.None);
        var prepared=FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        Assert.Equal("Received VIS-000001: Answer",Assert.Single(prepared.Recipients).Body);
    }
    [Fact]
    public async Task A_pending_message_translation_uses_the_original_template_for_the_whole_message()
    {
        var fixture=new FormActionFixture();var action=FormActionFixture.Action() with{Text=new Dictionary<string,string>{{"es","Recibimos {número}: {Visita}"}}};
        var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[action])]);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var prepared=FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        Assert.Equal("es",Assert.Single(prepared.Recipients).Culture);Assert.StartsWith("Recibimos",prepared.Recipients[0].Body,StringComparison.Ordinal);
    }
    [Fact]
    public async Task An_incomplete_alternative_pdf_makes_the_whole_notification_use_the_original_language()
    {
        var fixture=new FormActionFixture();fixture.Pdf.Cultures=["es"];
        var captured=fixture.Capture([new(Guid.NewGuid(),"Approved",[],[FormActionFixture.Action("GeneratePdf"),FormActionFixture.Action(includePdf:true)])]);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Approved",CancellationToken.None);
        var message=FormDraftPolicy.Read<FormActionSnapshot>(fixture.Runs.Runs[1].SnapshotJson);
        var recipient=Assert.Single(message.Recipients);Assert.Equal("es",recipient.Culture);Assert.StartsWith("Recibimos VIS-000001",recipient.Body,StringComparison.Ordinal);
        Assert.Equal(fixture.Runs.Runs[0].Id,message.PdfRunId);
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Every_event_uses_the_original_when_another_message_in_the_revision_is_untranslated(string original, string requested)
    {
        var fixture = new FormActionFixture(original);
        var received = FormActionFixture.Action();
        var approved = FormActionFixture.Action(includePdf: true);
        approved = approved with { Text = new Dictionary<string, string> { [original] = approved.Text![original] } };
        var captured = fixture.Capture([
            new(Guid.NewGuid(), "Received", [], [received]),
            new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf"), approved])
        ]);
        fixture.Recipients.People[captured.Record.ActorUserId] = new(captured.Record.ActorUserId, requested);

        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", TestContext.Current.CancellationToken);
        var confirmation = FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        Assert.Equal(original, Assert.Single(confirmation.Recipients).Culture);

        var decided = captured.Snapshot with
        {
            Decision = new(Guid.NewGuid(), Guid.NewGuid(), "Approver", fixture.Clock.GetUtcNow().UtcDateTime, "Approved", null)
        };
        await fixture.Recorder.RecordAsync(captured.Record, decided, "Approved", TestContext.Current.CancellationToken);
        var pdf = FormDraftPolicy.Read<FormActionSnapshot>(fixture.Runs.Runs.Single(run => run.Kind == "GeneratePdf").SnapshotJson);
        var notification = FormDraftPolicy.Read<FormActionSnapshot>(fixture.Runs.Runs.Single(run => run.Event == "Approved" && run.Kind == "NotifyAuthor").SnapshotJson);
        Assert.Equal(new[] { original }, pdf.Pdf!.Cultures);
        Assert.Equal(original, Assert.Single(notification.Recipients).Culture);
    }

    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task An_incomplete_pdf_design_also_makes_earlier_messages_use_the_original_language(string original, string requested)
    {
        var fixture = new FormActionFixture(original);
        var design = new FormPdfDesign(
            new Dictionary<string, string> { ["es"] = "Comprobante", ["en"] = "Receipt" },
            [fixture.Rules.FieldId], true,
            new Dictionary<string, string> { [original] = "Footer" }, true);
        fixture.CurrentDesign = new(Guid.NewGuid(), fixture.Rules.Definition.Id, 1, original,
            FormDraftPolicy.Serialize(design), fixture.Clock.GetUtcNow().UtcDateTime, new(Guid.NewGuid(), null));
        var captured = fixture.Capture([
            new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()]),
            new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf"), FormActionFixture.Action(includePdf: true)])
        ]);
        fixture.Recipients.People[captured.Record.ActorUserId] = new(captured.Record.ActorUserId, requested);

        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", TestContext.Current.CancellationToken);
        var confirmation = FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        Assert.Equal(original, Assert.Single(confirmation.Recipients).Culture);

        var decided = captured.Snapshot with
        {
            Decision = new(Guid.NewGuid(), Guid.NewGuid(), "Approver", fixture.Clock.GetUtcNow().UtcDateTime, "Approved", null)
        };
        await fixture.Recorder.RecordAsync(captured.Record, decided, "Approved", TestContext.Current.CancellationToken);
        var pdf = FormDraftPolicy.Read<FormActionSnapshot>(fixture.Runs.Runs.Single(run => run.Kind == "GeneratePdf").SnapshotJson);
        var notification = FormDraftPolicy.Read<FormActionSnapshot>(fixture.Runs.Runs.Single(run => run.Event == "Approved" && run.Kind == "NotifyAuthor").SnapshotJson);
        Assert.Equal(new[] { original }, pdf.Pdf!.Cultures);
        Assert.Equal(original, Assert.Single(notification.Recipients).Culture);
    }
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task A_missing_captured_form_name_makes_the_whole_event_use_the_original_even_with_a_translated_current_draft(string original, string requested)
    {
        var fixture = new FormActionFixture(original);
        var design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Comprobante", ["en"] = "Receipt" },
            [fixture.Rules.FieldId], true, null, true);
        fixture.CurrentDesign = new(Guid.NewGuid(), fixture.Rules.Definition.Id, 1, original,
            FormDraftPolicy.Serialize(design), fixture.Clock.GetUtcNow().UtcDateTime, new(Guid.NewGuid(), null));
        var captured = fixture.Capture([
            new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()]),
            new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])
        ]);
        var snapshot = captured.Snapshot with
        {
            Response = captured.Snapshot.Response with
            {
                Record = captured.Snapshot.Response.Record with
                {
                    NameJson = FormDraftPolicy.Serialize(new Dictionary<string, string> { [original] = "Original form" })
                }
            }
        };
        fixture.Recipients.People[captured.Record.ActorUserId] = new(captured.Record.ActorUserId, requested);

        await fixture.Recorder.RecordAsync(captured.Record, snapshot, "Received", TestContext.Current.CancellationToken);

        var confirmation = FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        Assert.Equal(original, Assert.Single(confirmation.Recipients).Culture);
    }

}
