using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormResponseAssistancePolicyTests
{
    [Fact]
    public async Task Unknown_historical_origin_does_not_invent_assistance()
    {
        var f = new FormRecordCaptureOriginFixture();
        var row = await CapturedAsync(f.Capture, f.Request);
        Assert.Null(FormResponseAssistancePolicy.Project(row, "es"));
    }

    [Fact]
    public async Task Included_mapping_and_changed_value_are_presented_as_saved_facts()
    {
        var f = new FormRecordCaptureOriginFixture(final: "corregido");
        var origin = f.Lineage();
        var row = await CapturedAsync(f.Capture, f.Request with { Origin = origin });
        var result = FormResponseAssistancePolicy.Project(row, "es");
        Assert.NotNull(result);
        var value = Assert.Single(result.Values);
        Assert.Equal("local", value.Proposed.Value);
        Assert.True(value.Included); Assert.True(value.Changed); Assert.True(value.Mapped);
        Assert.Equal("Assistant", result.SenderKind);
        Assert.Null(result.Operator);
    }

    [Fact]
    public async Task Excluded_mapping_is_neither_accepted_nor_changed_nor_mapped()
    {
        var f = new FormRecordCaptureOriginFixture(final: "corregido");
        var origin = f.Lineage() with { SenderKind = StructuredRecordSenderKind.Manual, OperatorUserId = f.OperatorId, IncludedFieldIds = [] };
        var row = await CapturedAsync(f.Capture, f.Request with { Origin = origin });
        var result = FormResponseAssistancePolicy.Project(row, "es");
        Assert.NotNull(result);
        var value = Assert.Single(result.Values);
        Assert.False(value.Included); Assert.False(value.Changed); Assert.False(value.Mapped);
        Assert.Equal(f.OperatorId, result.Operator?.UserId);
    }

    [Fact]
    public async Task Operator_confirmation_keeps_the_assistants_actual_reason_without_showing_technical_identifiers()
    {
        var f = new FormRecordCaptureOriginFixture();
        var origin = f.Assistant() with { SenderKind = StructuredRecordSenderKind.Manual, OperatorUserId = f.OperatorId };
        var row = await CapturedAsync(f.Capture, f.Request with { Origin = origin });
        var result = FormResponseAssistancePolicy.Project(row, "es");
        Assert.NotNull(result);
        Assert.Equal(origin.Reason, result.Reason);
        Assert.Equal("Manual", result.SenderKind);
        Assert.Equal(f.OperatorId, result.Operator?.UserId);
    }

    [Fact]
    public async Task Proposed_catalog_choice_uses_its_own_offered_historical_label_when_the_final_choice_changed()
    {
        var catalog = Guid.NewGuid(); var f = new BusinessFormCaptureFixture("choice", false, catalog);
        var field = Guid.ParseExact(f.Key[6..], "N"); var proposed = Guid.NewGuid(); var final = Guid.NewGuid();
        var offer = new StructuredFormCatalogOffer(field, f.Key, "Persona", catalog, 3, "Manual", "Personas",
            [new(proposed, "Nombre ofrecido", "Detalle ofrecido"), new(final, "Nombre final", null)]);
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, Guid.NewGuid(), null, null,
            [new(field, "choice", proposed.ToString("D"))], [field]);
        var row = await CapturedAsync(f, f.Request(new Dictionary<string, string> { [f.Key] = final.ToString("D") }) with { Catalogs = [offer], Origin = origin });
        var result = FormResponseAssistancePolicy.Project(row, "es");
        Assert.NotNull(result);
        var choice = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(result.Values).Proposed.Value);
        Assert.Equal("Nombre ofrecido", choice.Label);
        Assert.Equal("Detalle ofrecido", choice.Detail);
    }

    private static async Task<FormResponseStoredRow> CapturedAsync(BusinessFormCaptureFixture fixture, StructuredRecordCaptureRequest request)
    {
        var result = await fixture.Sink.CaptureAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal("Completed", result.State);
        var record = Assert.Single(fixture.Repository.Records);
        var row = new FormRecordStoredRow(record.SourceReceiptId, record.Id, record.Number, record.DefinitionId,
            fixture.Definition.Prefix, record.VersionId, fixture.Version.Number, fixture.Version.NameJson!, fixture.Version.SchemaJson,
            record.ValuesJson);
        return new(row, record.SubmittedAtUtc, new(record.ActorUserId, "Author")) { CaptureOriginJson = record.CaptureOriginJson };
    }
}
