using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldSourceTests
{
    [Fact]
    public void Sources_resolve_active_ids_codes_or_unambiguous_translated_text()
    {
        var sourceId = Guid.CreateVersion7();
        var first = Item(sourceId, "A", "First", "Translated");
        var second = Item(sourceId, "B", "Repeated", "Translated");
        var inactive = Item(sourceId, "C", "Inactive", null) with { IsActive = false };
        var index = FormCatalogFieldPolicy.CreateSourceIndex([first, second, inactive]);
        Assert.Equal(first.Id.ToString("D"), FormCatalogFieldPolicy.ResolveCatalogValue(index, first.Id.ToString("D")).Value);
        Assert.Equal(first.Id.ToString("D"), FormCatalogFieldPolicy.ResolveCatalogValue(index, " a ").Value);
        Assert.Equal(first.Id.ToString("D"), FormCatalogFieldPolicy.ResolveCatalogValue(index, "first").Value);
        Assert.Equal("Forms.Catalog.FieldValueInvalid", FormCatalogFieldPolicy.ResolveCatalogValue(index, "Translated").Error.Code);
        Assert.Equal("Forms.Catalog.FieldValueInvalid", FormCatalogFieldPolicy.ResolveCatalogValue(index, "C").Error.Code);
        Assert.Null(FormCatalogFieldPolicy.ResolveCatalogValue(index, " ").Value);
    }

    [Theory]
    [InlineData("CODE")]
    [InlineData("Source label")]
    public void Converting_a_loaded_text_field_to_catalog_resolves_codes_and_texts(string input)
    {
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "catalog", Guid.CreateVersion7(), false, false, null, null, null, "data", 0, true);
        var option = Item(field.SourceCatalogId!.Value, "CODE", "Source label", null);
        var item = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString("D")] = input })).IsSuccess);
        var context = new FormCatalogFieldContext([field], new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> { [field.SourceCatalogId.Value] = new[] { option } }, new HashSet<Guid> { field.Id }, new HashSet<Guid>());
        Assert.True(FormCatalogFieldPolicy.ValidateItems(catalog, [item], [], context).IsSuccess);
        Assert.Equal(option.Id.ToString("D"), JsonDocument.Parse(item.FieldValuesJson).RootElement.GetProperty(field.Id.ToString("D")).GetString());
    }

    [Fact]
    public void Empty_bounds_are_invalid_and_do_not_throw_or_drop_a_constraint()
    {
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Field" }, "integer", null, false, false, "", null, null, "data", 0, true);
        Assert.False(FormCatalogFieldPolicy.ValidRules(field));
    }

    private static FormCatalogStoredItemRow Item(Guid sourceId, string code, string text, string? english) => new(Guid.CreateVersion7(), sourceId, code, text, english, null, null, 0, true);
}
