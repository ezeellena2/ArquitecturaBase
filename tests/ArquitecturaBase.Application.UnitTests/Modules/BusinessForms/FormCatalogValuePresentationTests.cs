using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogValuePresentationTests
{
    [Theory]
    [InlineData("Integer", "123456789012345678")]
    [InlineData("Decimal", "1500.123456789012345678901234")]
    [InlineData("Date", "2026-10-08")]
    public void A_catalog_choice_preserves_its_captured_value_and_type_without_changing_its_label(string valueType, string value)
    {
        var fieldId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var catalogId = Guid.NewGuid();
        var recordId = Guid.NewGuid();
        var field = Field(fieldId, catalogId);
        var reference = new FormRecordReferenceRow(recordId, fieldId, catalogId, 4, itemId,
            "Texto histórico", "Detalle histórico", "Catálogo histórico", Value: value, ValueType: valueType);
        var row = new FormRecordStoredRow(Guid.NewGuid(), recordId, 1, Guid.NewGuid(), "VALUE", Guid.NewGuid(), 1,
            "{\"es\":\"Formulario\"}", JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web),
            JsonSerializer.Serialize(new[] { new { fieldId, value = itemId } }), [reference]);

        var projected = FormRecordPresentationPolicy.Project(row, "es", new(1, [field]));

        var choice = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(projected.Values).Value);
        Assert.Equal("Texto histórico", choice.Label);
        Assert.Equal("Detalle histórico", choice.Detail);
        Assert.Equal(value, choice.Value);
        Assert.Equal(valueType, choice.ValueType);
        Assert.DoesNotContain(value, choice.ToString(), StringComparison.Ordinal);
        var cell = Assert.Single(FormResponsePresentationPolicy.Cells([new(fieldId, "choice", "Zona", 1, [], valueType)], projected));
        Assert.Same(choice, cell.Value);
        Assert.Equal("Texto histórico", Assert.IsType<StructuredRecordChoiceValue>(cell.Value).Label);
    }

    [Theory]
    [InlineData("Integer")]
    [InlineData("Decimal")]
    [InlineData("Date")]
    public void Column_value_types_come_from_catalog_metadata_before_any_record_is_read(string valueType)
    {
        var catalogId = Guid.NewGuid();
        var question = Field(Guid.NewGuid(), catalogId);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{\"es\":\"Formulario\"}",
            JsonSerializer.Serialize(new FormVersionSchema(1, [question]), JsonSerializerOptions.Web), "es", ["es"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "VALUE", version.Id, [version],
            CatalogValueTypes: new Dictionary<Guid, string> { [catalogId] = valueType });

        var column = Assert.Single(FormResponsePresentationPolicy.Columns(definition, "es"));

        Assert.Equal("Zona", column.Label);
        Assert.Equal("choice", column.Type);
        Assert.Equal(valueType, column.ValueType);
        Assert.Empty(column.Options);
    }

    [Fact]
    public void Text_catalogs_and_legacy_choices_omit_the_new_optional_fields()
    {
        var choice = new StructuredRecordChoiceValue("historical-id", "Historical text");
        using var serialized = JsonDocument.Parse(JsonSerializer.Serialize(choice, JsonSerializerOptions.Web));
        Assert.Equal(["choiceId", "label"], serialized.RootElement.EnumerateObject().Select(property => property.Name));

        var catalogId = Guid.NewGuid();
        var question = Field(Guid.NewGuid(), catalogId);
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{\"es\":\"Formulario\"}",
            JsonSerializer.Serialize(new FormVersionSchema(1, [question]), JsonSerializerOptions.Web), "es", ["es"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "VALUE", version.Id, [version],
            CatalogValueTypes: new Dictionary<Guid, string> { [catalogId] = "Text" });
        var column = Assert.Single(FormResponsePresentationPolicy.Columns(definition, "es"));
        Assert.Null(column.ValueType);
        using var columnJson = JsonDocument.Parse(JsonSerializer.Serialize(column, JsonSerializerOptions.Web));
        Assert.False(columnJson.RootElement.TryGetProperty("valueType", out _));
    }

    private static CanonicalFormField Field(Guid fieldId, Guid catalogId) => new(fieldId, "zone", "choice",
        new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Zone" }, null, false, null, null, new(catalogId));
}
