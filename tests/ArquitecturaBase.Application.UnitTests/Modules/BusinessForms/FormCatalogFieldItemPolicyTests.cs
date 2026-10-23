using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

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
        var result = FormCatalogFieldPolicy.ValidateItems(catalog, items, [], NoRequest(), context);
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
        Assert.Equal("Forms.Catalog.FieldValueDuplicate", FormCatalogFieldPolicy.ValidateItems(catalog, [one, two], [], NoRequest(), context).Error.Code);
        Assert.True(catalog.SetManualItemActive(two, false).IsSuccess);
        Assert.True(FormCatalogFieldPolicy.ValidateItems(catalog, [one, two], [], NoRequest(), context).IsSuccess);
        Assert.Equal("5", JsonDocument.Parse(one.FieldValuesJson).RootElement.GetProperty(field.Id.ToString()).GetRawText());
    }

    [Fact]
    public void Type_conversion_reports_success_count_and_at_most_fifty_bad_rows()
    {
        var field = Field("integer");
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var items = Enumerable.Range(0, 62).Select(index => catalog.AddManualItem($"item{index}", "Item", null, null, null, index).Value).ToArray();
        foreach (var item in items.SkipLast(1)) Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = item == items[0] ? "1" : "invalid" })).IsSuccess);
        var context = new FormCatalogFieldContext([field], new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>(), new HashSet<Guid> { field.Id }, new HashSet<Guid>());
        var result = FormCatalogFieldPolicy.ValidateItems(catalog, items, [], NoRequest(), context);
        Assert.Equal("Forms.Catalog.FieldTypeNotConvertible", result.Error.Code);
        Assert.Equal(1, result.Error.Metadata!["convertedCount"]);
        Assert.Equal(60, result.Error.Metadata!["invalidCount"]);
        Assert.Equal(50, Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]).Count);
    }

    [Fact]
    public void Saved_values_that_point_to_an_inactive_source_option_are_kept()
    {
        var zones = Zones();
        var item = zones.Catalog.AddManualItem("acme", "Acme", null, null, null, 1).Value;
        Store(zones, item, zones.North.Id);

        var result = FormCatalogFieldPolicy.ValidateItems(zones.Catalog, [item], [], NoRequest(), zones.Context);

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Equal(zones.North.Id.ToString("D"), Value(zones, item));
    }

    [Fact]
    public void An_edited_option_that_keeps_its_inactive_source_value_can_be_saved()
    {
        var zones = Zones();
        var item = zones.Catalog.AddManualItem("acme", "Acme", null, null, null, 1).Value;
        Store(zones, item, zones.North.Id);

        var result = FormCatalogFieldPolicy.ValidateItems(zones.Catalog, [item], [Input(item.Id, "Acme S.A.", zones, zones.North.Id)],
            Request(item.Id, zones, zones.North.Id), zones.Context);

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Equal(zones.North.Id.ToString("D"), Value(zones, item));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_new_or_changed_value_needs_an_active_source_option(bool isNew)
    {
        var zones = Zones();
        var item = zones.Catalog.AddManualItem("acme", "Acme", null, null, null, 1).Value;
        if (!isNew) Store(zones, item, zones.South.Id);

        var result = FormCatalogFieldPolicy.ValidateItems(zones.Catalog, [item], [Input(item.Id, "Acme", zones, zones.North.Id)],
            Request(item.Id, zones, zones.North.Id), zones.Context);

        Assert.Equal("Forms.Catalog.FieldValueInvalid", result.Error.Code);
        Assert.Equal($"items[0].fields.{zones.Key}", Assert.Single(Assert.IsType<ValidationError>(result.Error).Errors.Keys));
    }

    [Fact]
    public void Recording_items_keeps_saved_values_of_inactive_source_options()
    {
        var zones = Zones();
        var untouched = zones.Catalog.AddManualItem("acme", "Acme", null, null, null, 1).Value;
        var edited = zones.Catalog.AddManualItem("globex", "Globex", null, null, null, 2).Value;
        Store(zones, untouched, zones.North.Id);
        Store(zones, edited, zones.North.Id);
        var recorder = new FormCatalogItemRecorder(new FormCatalogCapacityRepository(zones.Catalog, [untouched, edited]));

        var result = recorder.Record(zones.Catalog, [untouched, edited],
            [Input(edited.Id, "Globex S.A.", zones, zones.North.Id), Input(null, "Initech", zones, zones.South.Id)], fieldContext: zones.Context);

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Equal(zones.North.Id.ToString("D"), Value(zones, untouched));
        Assert.Equal(zones.North.Id.ToString("D"), Value(zones, edited));
        Assert.Equal(zones.South.Id.ToString("D"), Value(zones, result.Value.Items.Single(item => item.Code == "initech")));
    }

    [Fact]
    public void Recording_a_changed_value_still_needs_an_active_source_option()
    {
        var zones = Zones();
        var item = zones.Catalog.AddManualItem("acme", "Acme", null, null, null, 1).Value;
        Store(zones, item, zones.South.Id);
        var recorder = new FormCatalogItemRecorder(new FormCatalogCapacityRepository(zones.Catalog, [item]));

        var result = recorder.Record(zones.Catalog, [item], [Input(item.Id, "Acme", zones, zones.North.Id)], fieldContext: zones.Context);

        Assert.Equal("Forms.Catalog.FieldValueInvalid", result.Error.Code);
        Assert.Equal($"items[0].fields.{zones.Key}", Assert.Single(Assert.IsType<ValidationError>(result.Error).Errors.Keys));
    }

    private static BusinessFormCatalogField Field(string type) => new(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Campo" }, type,
        type == "catalog" ? Guid.CreateVersion7() : null, false, false, null, null, type == "text" ? 300 : null, "data", 0, true);

    private static Dictionary<Guid, Dictionary<string, JsonElement>> NoRequest() => [];

    private sealed record ZoneScenario(BusinessFormCatalog Catalog, BusinessFormCatalogField Field, FormCatalogStoredItemRow North,
        FormCatalogStoredItemRow South, FormCatalogFieldContext Context)
    {
        public string Key => Field.Id.ToString("D");
    }

    private static ZoneScenario Zones()
    {
        var field = Field("catalog") with { Key = "zone" };
        var sourceId = field.SourceCatalogId!.Value;
        var catalog = BusinessFormCatalog.CreateManual(new Dictionary<string, string> { ["es"] = "Proveedores" }, new Dictionary<string, string>());
        var north = new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "N", "Norte", null, null, null, 1, false);
        var south = new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "S", "Sur", null, null, null, 2, true);
        var context = new FormCatalogFieldContext([field], new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> { [sourceId] = [north, south] },
            new HashSet<Guid>(), new HashSet<Guid>());
        return new(catalog, field, north, south, context);
    }

    private static void Store(ZoneScenario zones, BusinessFormCatalogItem item, Guid option) => Assert.True(zones.Catalog.SetManualItemFields(item,
        JsonSerializer.Serialize(new Dictionary<string, string> { [zones.Key] = option.ToString("D") }, JsonSerializerOptions.Web)).IsSuccess);

    private static FormCatalogManualItemSaveRequest Input(Guid? itemId, string text, ZoneScenario zones, Guid option) =>
        new(itemId is null ? "new-" + text : null, itemId, text.Split(' ')[0].ToLowerInvariant(), new Dictionary<string, string?> { ["es"] = text }, null, true,
            Fields: new Dictionary<string, string?> { [zones.Key] = option.ToString("D") });

    private static Dictionary<Guid, Dictionary<string, JsonElement>> Request(Guid itemId, ZoneScenario zones, Guid option) => new()
    {
        [itemId] = new(StringComparer.Ordinal) { [zones.Key] = JsonSerializer.SerializeToElement(option.ToString("D"), JsonSerializerOptions.Web) },
    };

    private static string? Value(ZoneScenario zones, BusinessFormCatalogItem item) =>
        FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson).TryGetValue(zones.Key, out var value) ? value.GetString() : null;

    private static string Describe(Result result) => result.IsSuccess ? "Success" :
        $"{result.Error.Code} at {string.Join(", ", (result.Error as ValidationError)?.Errors.Keys ?? [])}";
}
