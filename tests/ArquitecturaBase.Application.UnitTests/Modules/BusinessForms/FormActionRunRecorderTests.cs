using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormActionRunRecorderTests
{
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
}
