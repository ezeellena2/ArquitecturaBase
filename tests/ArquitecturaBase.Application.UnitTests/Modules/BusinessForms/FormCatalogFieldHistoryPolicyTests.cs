using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldHistoryPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid DefinitionId = Guid.Parse("00000000-0000-7000-8000-0000000000d1");
    private static readonly Guid CatalogId = Guid.Parse("00000000-0000-7000-8000-0000000000c1");
    private static readonly Guid FieldA = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid FieldB = Guid.Parse("00000000-0000-7000-8000-0000000000a2");

    private static FormRecordReferenceRow Reference(string fields) => new(Guid.CreateVersion7(), Guid.CreateVersion7(), CatalogId, 1,
        Guid.CreateVersion7(), "Norte", null, "Sucursales", FieldValuesJson: fields);

    private static string Field(Guid id, string key, string name, int value) =>
        $$"""{"id":"{{id}}","key":"{{key}}","name":{"es":"{{name}}"},"type":"integer","role":"data","value":{{value}}}""";

    [Fact]
    public async Task A_capture_writes_one_row_per_catalog_and_field_and_a_later_response_replaces_it()
    {
        var repository = new BusinessFormCaptureFixture("shortText", false).Repository;
        var first = Reference($"[{Field(FieldA, "days", "Días", 1)},{Field(FieldB, "kg", "Kilos", 2)}]");
        var same = Reference($"[{Field(FieldA, "days", "Días", 3)}]");

        await FormCatalogFieldHistoryPolicy.RecordAsync(repository, DefinitionId, 1, [first, same], Ct);
        await FormCatalogFieldHistoryPolicy.RecordAsync(repository, DefinitionId, 2, [Reference($"[{Field(FieldA, "days", "Días nuevos", 1)}]")], Ct);
        await FormCatalogFieldHistoryPolicy.RecordAsync(repository, DefinitionId, 1, [Reference($"[{Field(FieldA, "days", "Días viejos", 1)}]")], Ct);

        Assert.Equal(2, repository.CatalogFieldHistory.Count);
        var days = repository.CatalogFieldHistory.Single(value => value.FieldId == FieldA);
        Assert.Equal(2, days.RecordNumber);
        Assert.Equal("Días nuevos", System.Text.Json.JsonDocument.Parse(days.NameJson).RootElement.GetProperty("es").GetString());
        Assert.Equal(1, repository.CatalogFieldHistory.Single(value => value.FieldId == FieldB).Position);
    }

    [Fact]
    public async Task A_reference_without_sealed_fields_writes_nothing()
    {
        var repository = new BusinessFormCaptureFixture("shortText", false).Repository;

        await FormCatalogFieldHistoryPolicy.RecordAsync(repository, DefinitionId, 1, [new(Guid.CreateVersion7(), Guid.CreateVersion7(), CatalogId, 1,
            Guid.CreateVersion7(), "Norte", null, "Sucursales")], Ct);

        Assert.Empty(repository.CatalogFieldHistory);
    }
}
