using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class BusinessFormRecordCaptureOriginTests
{
    [Fact]
    public async Task Capture_persists_the_exact_offer_and_what_the_person_changed()
    {
        var f = new FormRecordCaptureOriginFixture(final: "corregido");
        var request = f.Request with { Origin = f.Assistant("propuesto") };
        var result = await f.Capture.Sink.CaptureAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Capture.Repository.Records);
        Assert.NotNull(record.CaptureOriginJson);
        var stored = FormDraftPolicy.Read<FormRecordCaptureOriginSnapshot>(record.CaptureOriginJson);
        Assert.Equal(request.Origin.Proposal, stored.Origin.Proposal);
        Assert.Null(stored.Origin.OperatorUserId);
        Assert.Equal([f.Field.FieldId], stored.ChangedFieldIds);
        Assert.Equal("propuesto", Assert.Single(stored.Origin.ProposedValues).Value);
    }

    [Fact]
    public async Task Existing_capture_without_origin_remains_unknown()
    {
        var f = new FormRecordCaptureOriginFixture();
        var result = await f.Capture.Sink.CaptureAsync(f.Request, TestContext.Current.CancellationToken);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Capture.Repository.Records);
        Assert.Null(record.CaptureOriginJson);
        Assert.Null(record.OriginRecordId);
        Assert.Null(record.OriginSuggestionId);
    }

    [Fact]
    public async Task Invalid_origin_is_rejected_before_allocating_a_number_or_running_rules()
    {
        var f = new FormRecordCaptureOriginFixture();
        var request = f.Request with { Origin = f.Assistant() with { OperatorUserId = f.OperatorId } };
        var result = await f.Capture.Sink.CaptureAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Empty(f.Capture.Repository.Records);
        Assert.Empty(f.Capture.Repository.Values);
        var valid = await f.Capture.Sink.CaptureAsync(f.Request with { SourceReceiptId = Guid.NewGuid() }, TestContext.Current.CancellationToken);
        Assert.Equal(1, valid.Number);
    }

    [Fact]
    public async Task Always_lineage_is_saved_with_the_actual_source_record_and_suggestion()
    {
        var f = new FormRecordCaptureOriginFixture();
        var origin = f.Lineage();
        var result = await f.Capture.Sink.CaptureAsync(f.Request with { Origin = origin }, TestContext.Current.CancellationToken);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Capture.Repository.Records);
        Assert.Equal(origin.Suggestion!.OriginRecordId, record.OriginRecordId);
        Assert.Equal(origin.Suggestion.OriginSuggestionId, record.OriginSuggestionId);
    }

    [Fact]
    public async Task Same_receipt_with_different_origin_is_a_conflict_instead_of_replaying_another_offer()
    {
        var f = new FormRecordCaptureOriginFixture();
        var request = f.Request with { Origin = f.Assistant() };
        Assert.Equal("Completed", (await f.Capture.Sink.CaptureAsync(request, TestContext.Current.CancellationToken)).State);
        var conflict = await f.Capture.Sink.CaptureAsync(request with { Origin = f.Manual() }, TestContext.Current.CancellationToken);
        Assert.Equal("Rejected", conflict.State);
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, conflict.ErrorCode);
        Assert.Single(f.Capture.Repository.Records);
    }

    [Fact]
    public async Task A_changed_catalog_answer_keeps_the_proposed_options_own_historical_presentation()
    {
        var catalog = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("choice", false, catalog);
        var field = Guid.ParseExact(f.Key[6..], "N");
        var proposed = Guid.NewGuid();
        var final = Guid.NewGuid();
        var offer = new StructuredFormCatalogOffer(field, f.Key, "Persona", catalog, 3, "Manual", "Personas",
            [new(proposed, "Nombre propuesto", "Detalle"), new(final, "Nombre final", null)]);
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, Guid.NewGuid(), null, null,
            [new(field, "choice", proposed.ToString("D"))], [field]);
        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = final.ToString("D") })
            with { Catalogs = [offer], Origin = origin }, TestContext.Current.CancellationToken);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Repository.Records);
        Assert.NotNull(record.CaptureOriginJson);
        var stored = FormDraftPolicy.Read<FormRecordCaptureOriginSnapshot>(record.CaptureOriginJson);
        var reference = Assert.Single(stored.ProposedReferences);
        Assert.Equal(record.Id, reference.RecordId);
        Assert.Equal(proposed, reference.ItemId);
        Assert.Equal("Nombre propuesto", reference.Text);
        Assert.Equal(final, Assert.Single(f.Repository.References).ItemId);
        Assert.Equal([field], stored.ChangedFieldIds);
    }
}
