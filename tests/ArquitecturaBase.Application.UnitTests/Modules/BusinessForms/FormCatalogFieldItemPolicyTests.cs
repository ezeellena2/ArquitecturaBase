using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldItemPolicyTests
{
    [Fact]
    public void Required_transition_reports_all_missing_active_items()
    {
        var field = Field("text") with { Required = true };
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var items = new[] { catalog.AddManualItem("one", "One", null, null, null, 1).Value, catalog.AddManualItem("two", "Two", null, null, null, 2).Value };
        var context = new FormCatalogFieldContext([field], new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>(), new HashSet<Guid>(), new HashSet<Guid> { field.Id });
        var result = FormCatalogFieldPolicy.ValidateItems(catalog, items, [], context);
        Assert.Equal("Forms.Catalog.FieldValuesMissing", result.Error.Code);
        Assert.Equal(2, result.Error.Metadata!["missingCount"]);
    }

    [Fact]
    public void Unique_fields_only_compare_active_items_and_write_canonical_json()
    {
        var field = Field("integer") with { Unique = true };
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var one = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        var two = catalog.AddManualItem("two", "Two", null, null, null, 2).Value;
        Assert.True(catalog.SetManualItemFields(one, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = "0005" })).IsSuccess);
        Assert.True(catalog.SetManualItemFields(two, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = "5" })).IsSuccess);
        var context = new FormCatalogFieldContext([field], new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>(), new HashSet<Guid>(), new HashSet<Guid>());
        Assert.Equal("Forms.Catalog.FieldValueDuplicate", FormCatalogFieldPolicy.ValidateItems(catalog, [one, two], [], context).Error.Code);
        Assert.True(catalog.SetManualItemActive(two, false).IsSuccess);
        Assert.True(FormCatalogFieldPolicy.ValidateItems(catalog, [one, two], [], context).IsSuccess);
        Assert.Equal("5", JsonDocument.Parse(one.FieldValuesJson).RootElement.GetProperty(field.Id.ToString()).GetRawText());
    }

    [Fact]
    public void Type_conversion_reports_success_count_and_at_most_fifty_bad_rows()
    {
        var field = Field("integer");
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var items = Enumerable.Range(0, 61).Select(index => catalog.AddManualItem($"item{index}", "Item", null, null, null, index).Value).ToArray();
        foreach (var item in items) Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = item == items[0] ? "1" : "invalid" })).IsSuccess);
        var context = new FormCatalogFieldContext([field], new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>(), new HashSet<Guid> { field.Id }, new HashSet<Guid>());
        var result = FormCatalogFieldPolicy.ValidateItems(catalog, items, [], context);
        Assert.Equal("Forms.Catalog.FieldTypeNotConvertible", result.Error.Code);
        Assert.Equal(1, result.Error.Metadata!["convertedCount"]);
        Assert.Equal(50, Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]).Count);
    }

    private static BusinessFormCatalogField Field(string type) => new(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Campo" }, type,
        type == "catalog" ? Guid.CreateVersion7() : null, false, false, null, null, type == "text" ? 300 : null, "data", 0, true);
}
