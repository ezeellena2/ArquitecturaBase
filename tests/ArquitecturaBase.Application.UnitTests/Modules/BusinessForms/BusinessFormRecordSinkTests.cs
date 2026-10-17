using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRecordSinkTests
{
    [Fact]
    public async Task Capture_fixes_the_rule_revision_and_creates_received_runs_before_the_same_commit()
    {
        BusinessFormCaptureFixture? fixture = null;
        fixture = new("shortText", false, onCommit: () =>
        {
            Assert.Single(fixture!.Repository.Records);
            Assert.Single(fixture.Repository.Receipts);
            Assert.Single(fixture.Actions.Runs.Runs);
        });
        var action = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string,string> { ["es"]="Recibimos {número}: {Formulario}" }, false);
        var rule = new FormRule(Guid.NewGuid(), "Received", [], [action]);
        var ruleSet = BusinessFormRuleSet.Create(fixture.Definition.Id, 1, "es", FormDraftPolicy.Serialize(new[] { rule }), false, fixture.Version.SchemaJson);
        fixture.Actions.Rules.RuleSets.Add(ruleSet); fixture.Definition.SetActiveRuleSetId(ruleSet.Id);
        var request = fixture.Request(new Dictionary<string,string> { [fixture.Key]="Valor fijo" });
        var captured = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        Assert.Equal("Completed", captured.State); Assert.Equal(captured, replay); Assert.Equal(1, fixture.UnitOfWork.Transactions);
        var record = Assert.Single(fixture.Repository.Records); Assert.Equal(ruleSet.Id, record.RuleSetId);
        var run = Assert.Single(fixture.Actions.Runs.Runs); Assert.Equal(record.Id, run.RecordId); Assert.Equal(ruleSet.Id, run.RuleSetId);
        var snapshot = FormDraftPolicy.Read<FormActionSnapshot>(run.SnapshotJson);
        Assert.Equal(record.ActorUserId, Assert.Single(snapshot.Recipients).UserId);
        Assert.DoesNotContain("valuesJson", run.SnapshotJson, StringComparison.Ordinal);
        Assert.Contains("Valor fijo", Assert.Single(snapshot.Recipients).Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_replayed_session_returns_the_same_record_and_other_answers_do_not_consume_another_number()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", required: false);
        var answers = new Dictionary<string, string> { [fixture.Key] = "answer" };
        var request = fixture.Request(answers);

        var first = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var different = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "other" }), CancellationToken.None);

        Assert.Equal("Completed", first.State);
        Assert.Equal(first.RecordId, replay.RecordId);
        Assert.Equal(first.Number, replay.Number);
        Assert.Equal("Rejected", different.State);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
        Assert.Single(fixture.Repository.Records);
    }

    [Fact]
    public async Task A_prior_receipt_can_be_recovered_without_permission_or_answer_values()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", required: false);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = "private answer" });
        var captured = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        fixture.Permission.Allowed = false;

        var receipt = await fixture.Sink.FindExistingReceiptAsync(request, CancellationToken.None);
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var changedAnswers = await fixture.Sink.FindExistingReceiptAsync(request with
        {
            Answers = new Dictionary<string, string> { [fixture.Key] = "different answer" },
        }, CancellationToken.None);

        Assert.Equal("Completed", receipt!.State);
        Assert.Equal(captured.RecordId, receipt.RecordId);
        Assert.Equal(captured.Number, receipt.Number);
        Assert.Equal(captured, replay);
        Assert.Null(changedAnswers);
        Assert.DoesNotContain("private answer", receipt.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_required_text_answer_cannot_be_empty_and_does_not_allocate_a_number()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "" }), CancellationToken.None);

        Assert.Equal("Rejected", result.State);
        Assert.Null(result.Number);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
        Assert.Empty(fixture.Repository.Records);
    }

    [Fact]
    public async Task A_decimal_with_more_precision_than_decimal_can_represent_is_rejected_without_rounding_or_numbering()
    {
        var fixture = new BusinessFormCaptureFixture("decimal", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string>
        {
            [fixture.Key] = "0.123456789012345678901234567890123456789",
        }), CancellationToken.None);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
        Assert.Empty(fixture.Repository.Records);
    }

    [Fact]
    public async Task An_exact_decimal_at_scale_28_is_preserved()
    {
        var fixture = new BusinessFormCaptureFixture("decimal", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string>
        {
            [fixture.Key] = "0.0000000000000000000000000001",
        }), CancellationToken.None);

        Assert.Equal("Completed", result.State);
        Assert.Equal(0.0000000000000000000000000001m, Assert.Single(fixture.Repository.Values).DecimalValue);
    }

    [Fact]
    public async Task Boolean_false_is_saved_as_a_value()
    {
        var fixture = new BusinessFormCaptureFixture("boolean", required: true);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "false" }), CancellationToken.None);

        Assert.Equal("Completed", result.State);
        Assert.False(Assert.Single(fixture.Repository.Values).BooleanValue);
    }

    [Fact]
    public async Task An_optional_missing_answer_is_absent_from_the_typed_projection()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", required: false);
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string>()), CancellationToken.None);

        Assert.Equal("Completed", result.State);
        Assert.Empty(fixture.Repository.Values);
        Assert.Equal("[]", Assert.Single(fixture.Repository.Records).ValuesJson);
    }

    [Fact]
    public async Task A_permission_revoked_after_the_request_check_is_rejected_inside_the_capture_transaction()
    {
        var fixture = new BusinessFormCaptureFixture("shortText", required: false);
        fixture.Permission.RevokeOnSecondCheck = true;
        var result = await fixture.Sink.CaptureAsync(fixture.Request(new Dictionary<string, string> { [fixture.Key] = "answer" }), CancellationToken.None);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.AuthorizationChanged.Code, result.ErrorCode);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
        Assert.Empty(fixture.Repository.Records);
        Assert.Single(fixture.Repository.Receipts);
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("Filters")]
    public async Task An_offered_catalog_option_keeps_its_reference_and_original_text(string kind)
    {
        var catalog = Guid.NewGuid();
        var fixture = new BusinessFormCaptureFixture("choice", true, catalog);
        var item = Guid.NewGuid();
        var fieldId = Guid.ParseExact(fixture.Key[6..], "N");
        var offer = new StructuredFormCatalogOffer(fieldId, fixture.Key, "Persona", catalog, 3, kind, "Personas",
            [new(item, "Original", "Detail")]);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = item.ToString("D") }) with { Catalogs = [offer] };
        fixture.Repository.ItemActive = kind == "Manual";
        var result = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        Assert.Equal("Completed", result.State);
        Assert.Equal(result, replay);
        var reference = Assert.Single(fixture.Repository.References);
        Assert.Equal(item, reference.ItemId);
        Assert.Equal(3, reference.CatalogRevision);
        Assert.Equal("Original", reference.Text);
        Assert.Equal("Detail", reference.Detail);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_deactivated_manual_option_rejects_the_whole_response_without_a_number()
    {
        var catalog = Guid.NewGuid();
        var fixture = new BusinessFormCaptureFixture("choice", true, catalog);
        var item = Guid.NewGuid();
        var fieldId = Guid.ParseExact(fixture.Key[6..], "N");
        var offer = new StructuredFormCatalogOffer(fieldId, fixture.Key, "Persona", catalog, 1, "Manual", "Personas", [new(item, "Private", null)]);
        fixture.Repository.ItemActive = false;
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = item.ToString("D") }) with { Catalogs = [offer] };
        var result = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, result.ErrorCode);
        Assert.Equal(fieldId, result.RejectedFieldId);
        fixture.ReceiptReader.SkipRead = true;
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        Assert.Equal(result, replay);
        Assert.Empty(fixture.Repository.Records);
        Assert.Empty(fixture.Repository.Values);
        Assert.Empty(fixture.Repository.References);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task An_option_not_in_the_session_offer_is_rejected()
    {
        var catalog = Guid.NewGuid();
        var fixture = new BusinessFormCaptureFixture("choice", true, catalog);
        var offer = new StructuredFormCatalogOffer(Guid.ParseExact(fixture.Key[6..], "N"), fixture.Key, "Persona", catalog, 1,
            "Filters", "Personas", [new(Guid.NewGuid(), "Offered", null)]);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = Guid.NewGuid().ToString("D") }) with { Catalogs = [offer] };
        Assert.Equal("Rejected", (await fixture.Sink.CaptureAsync(request, CancellationToken.None)).State);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
        Assert.Empty(fixture.Repository.References);
    }

    [Fact]
    public async Task Files_are_committed_with_the_record_and_a_changed_replay_never_allocates_another_number()
    {
        var fixture = new BusinessFormCaptureFixture("photo", true);
        var request = fixture.Request(new Dictionary<string, string>()) with { Files = [new(Guid.ParseExact(fixture.Key[6..], "N"), 0,
            "123", "photo", "photo.png", "image/png", new string('a', 64), [1, 2, 3])] };
        var saved = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        Assert.Equal("Completed", saved.State);
        Assert.Single(fixture.StoredFiles.Files);
        Assert.Single(fixture.Repository.FileReferences);
        Assert.Empty(fixture.Repository.Values);
        var replay = await fixture.Sink.CaptureAsync(request, CancellationToken.None);
        Assert.Equal(saved, replay);
        Assert.Single(fixture.StoredFiles.Files);
        var changed = await fixture.Sink.CaptureAsync(request with { Files = [request.Files![0] with { Name = "changed.png" }] }, CancellationToken.None);
        Assert.Equal("Rejected", changed.State);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task An_invalid_file_rejects_every_value_without_creating_a_record_or_consuming_a_number()
    {
        var fixture = new BusinessFormCaptureFixture("photo", true);
        var request = fixture.Request(new Dictionary<string, string>()) with { Files = [new(Guid.ParseExact(fixture.Key[6..], "N"), 0,
            "123", "photo", "photo.png", "image/png", "bad", [1, 2, 3])] };
        Assert.Equal("Rejected", (await fixture.Sink.CaptureAsync(request, CancellationToken.None)).State);
        Assert.Empty(fixture.Repository.Records);
        Assert.Empty(fixture.StoredFiles.Files);
        Assert.Equal(0, fixture.Definition.LastRecordNumber);
    }

}
