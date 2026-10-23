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
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var items = new[] { catalog.AddManualItem("one", "One", null, null, null, 1).Value, catalog.AddManualItem("two", "Two", null, null, null, 2).Value };
        var context = Context(field, requiredTransition: true);
        var result = FormCatalogFieldPolicy.ValidateItems(catalog, items, [], NoRequest(), context);
        Assert.Equal("Forms.Catalog.FieldValuesMissing", result.Error.Code);
        Assert.Equal(2, result.Error.Metadata!["missingCount"]);
    }

    [Fact]
    public void Unique_fields_only_compare_active_items_and_write_canonical_json()
    {
        var field = Field("integer") with { Unique = true };
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var one = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        var two = catalog.AddManualItem("two", "Two", null, null, null, 2).Value;
        Assert.True(catalog.SetManualItemFields(one, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = "0005" })).IsSuccess);
        Assert.True(catalog.SetManualItemFields(two, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = "5" })).IsSuccess);
        var context = Context(field);
        Assert.Equal("Forms.Catalog.FieldValueDuplicate", FormCatalogFieldPolicy.ValidateItems(catalog, [one, two], [], NoRequest(), context).Error.Code);
        Assert.True(catalog.SetManualItemActive(two, false).IsSuccess);
        Assert.True(FormCatalogFieldPolicy.ValidateItems(catalog, [one, two], [], NoRequest(), context).IsSuccess);
        Assert.Equal("5", JsonDocument.Parse(one.FieldValuesJson).RootElement.GetProperty(field.Id.ToString()).GetRawText());
    }

    [Fact]
    public void Type_conversion_reports_success_count_and_at_most_fifty_bad_rows()
    {
        var field = Field("integer");
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var items = Enumerable.Range(0, 62).Select(index => catalog.AddManualItem($"item{index}", "Item", null, null, null, index).Value).ToArray();
        foreach (var item in items.SkipLast(1)) Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString()] = item == items[0] ? "1" : "invalid" })).IsSuccess);
        var context = Context(field, previousType: "text");
        var result = FormCatalogFieldPolicy.ValidateItems(catalog, items, [], NoRequest(), context);
        Assert.Equal("Forms.Catalog.FieldTypeNotConvertible", result.Error.Code);
        Assert.Equal(1, result.Error.Metadata!["convertedCount"]);
        Assert.Equal(60, result.Error.Metadata!["invalidCount"]);
        Assert.Equal(50, Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]).Count);
    }

    [Fact]
    public void Type_conversion_reports_the_first_fifty_bad_rows_in_row_order_whatever_the_stored_order()
    {
        var field = Field("integer");
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var items = Enumerable.Range(1, 62).Select(position => catalog.AddManualItem($"item{position}", "Item", null, null, null, position).Value).ToArray();
        foreach (var item in items) StoreText(catalog, item, field, "invalid");
        var stored = items.Where((_, index) => index % 2 == 1).Reverse().Concat(items.Where((_, index) => index % 2 == 0)).ToArray();
        var context = Context(field, previousType: "text");

        var result = FormCatalogFieldPolicy.ValidateItems(catalog, stored, [], NoRequest(), context);

        Assert.Equal(62, result.Error.Metadata!["invalidCount"]);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]);
        Assert.Equal(Enumerable.Range(1, 50), rows.Select(row => row.Row));
    }

    [Fact]
    public void Type_conversion_rows_carry_their_row_attempted_value_and_reason()
    {
        var field = Field("integer") with { Required = true, Max = "180" };
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Plazos" }, new Dictionary<string, string>());
        var days = catalog.AddManualItem("days", "Days", null, null, null, 3).Value;
        var large = catalog.AddManualItem("large", "Large", null, null, null, 4).Value;
        var empty = catalog.AddManualItem("empty", "Empty", null, null, null, 5).Value;
        StoreText(catalog, days, field, "30 días");
        StoreText(catalog, large, field, "200");
        var context = Context(field, previousType: "text");

        var result = FormCatalogFieldPolicy.ValidateItems(catalog, [days, large, empty], [], NoRequest(), context);

        Assert.Equal("Forms.Catalog.FieldTypeNotConvertible", result.Error.Code);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]);
        Assert.Equal(new (int, string?, string)[] { (3, "30 días", "invalid"), (4, "200", "range"), (5, null, "required") },
            rows.Select(row => (row.Row, row.Value, row.Reason)));
        Assert.Equal(new[] { days.Id, large.Id, empty.Id }, rows.Select(row => row.ItemId));
        Assert.DoesNotContain("30 días", rows[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Type_conversion_to_a_shorter_text_reports_length()
    {
        var field = Field("text") with { MaxLength = 3 };
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Montos" }, new Dictionary<string, string>());
        var item = catalog.AddManualItem("amount", "Amount", null, null, null, 1).Value;
        Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, long> { [field.Id.ToString("D")] = 12345 })).IsSuccess);
        var context = Context(field, previousType: "integer");

        var result = FormCatalogFieldPolicy.ValidateItems(catalog, [item], [], NoRequest(), context);

        var row = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]));
        Assert.Equal<(int, string?, string)>((1, "12345", "length"), (row.Row, row.Value, row.Reason));
    }

    [Fact]
    public void Converting_text_to_decimal_reads_the_request_culture_and_leaves_ambiguous_values_unconverted()
    {
        var field = Field("decimal");
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Precios" }, new Dictionary<string, string>());
        var items = new[] { "1.500", "12.345", "1,5", "1.234,5" }.Select((saved, index) =>
        {
            var item = catalog.AddManualItem($"item{index + 1}", "Item", null, null, null, index + 1).Value;
            StoreText(catalog, item, field, saved);
            return item;
        }).ToArray();

        var result = FormCatalogFieldPolicy.ValidateItems(catalog, items, [], NoRequest(), Context(field, previousType: "text", culture: "es"));

        Assert.Equal("Forms.Catalog.FieldTypeNotConvertible", result.Error.Code);
        Assert.Equal(2, result.Error.Metadata!["convertedCount"]);
        Assert.Equal(2, result.Error.Metadata!["invalidCount"]);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]);
        Assert.Equal(new (int, string?, string)[] { (1, "1.500", "invalid"), (2, "12.345", "invalid") },
            rows.Select(row => (row.Row, row.Value, row.Reason)));
    }

    [Theory]
    [InlineData("decimal", "es", "1,5", "1.5")]
    [InlineData("decimal", "es", "1.234,5", "1234.5")]
    [InlineData("decimal", "en", "1,500", "1500")]
    [InlineData("decimal", "en", "1.5", "1.5")]
    [InlineData("integer", "es", "1.500", "1500")]
    [InlineData("date", "es", "15/03/2027", "2027-03-15")]
    [InlineData("boolean", "es", "Sí", "true")]
    public void Converting_text_reads_saved_values_as_written_in_the_request_culture(string type, string culture, string saved, string expected)
    {
        var field = Field(type);
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var item = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        StoreText(catalog, item, field, saved);

        var result = FormCatalogFieldPolicy.ValidateItems(catalog, [item], [], NoRequest(), Context(field, previousType: "text", culture: culture));

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Equal(expected, StoredInput(item, field));
    }

    [Fact]
    public void Converting_a_saved_decimal_to_integer_keeps_the_canonical_reading()
    {
        var field = Field("integer");
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var item = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        StoreText(catalog, item, field, "1.234");

        var result = FormCatalogFieldPolicy.ValidateItems(catalog, [item], [], NoRequest(), Context(field, previousType: "decimal", culture: "es"));

        Assert.Equal("Forms.Catalog.FieldTypeNotConvertible", result.Error.Code);
        var row = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<FormCatalogInvalidFieldRow>>(result.Error.Metadata!["invalidRows"]));
        Assert.Equal<(int, string?, string)>((1, "1.234", "invalid"), (row.Row, row.Value, row.Reason));
    }

    [Fact]
    public void A_value_edited_while_converting_is_read_as_the_new_type()
    {
        var field = Field("decimal");
        var key = field.Id.ToString("D");
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Catalog" }, new Dictionary<string, string>());
        var item = catalog.AddManualItem("one", "One", null, null, null, 1).Value;
        StoreText(catalog, item, field, "doce");
        var requested = new Dictionary<Guid, Dictionary<string, JsonElement>>
        {
            [item.Id] = new(StringComparer.Ordinal) { [key] = JsonSerializer.SerializeToElement("12.345", JsonSerializerOptions.Web) },
        };

        var result = FormCatalogFieldPolicy.ValidateItems(catalog, [item], [], requested, Context(field, previousType: "text", culture: "es"));

        Assert.True(result.IsSuccess, Describe(result));
        Assert.Equal("12.345", StoredInput(item, field));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_saved_value_that_points_to_no_source_option_is_rejected(bool sent)
    {
        var zones = Zones();
        var item = zones.Catalog.AddManualItem("acme", "Acme", null, null, null, 1).Value;
        var otherCatalogOption = Guid.CreateVersion7();
        Store(zones, item, otherCatalogOption);

        var result = sent
            ? FormCatalogFieldPolicy.ValidateItems(zones.Catalog, [item], [Input(item.Id, "Acme", zones, otherCatalogOption)],
                Request(item.Id, zones, otherCatalogOption), zones.Context)
            : FormCatalogFieldPolicy.ValidateItems(zones.Catalog, [item], [], NoRequest(), zones.Context);

        Assert.Equal("Forms.Catalog.FieldValueInvalid", result.Error.Code);
        Assert.Equal(sent ? $"items[0].fields.{zones.Key}" : "fields[0].type",
            Assert.Single(Assert.IsType<ValidationError>(result.Error).Errors.Keys));
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

    // A previous type means the field is changing type in this save; the saved values were written under that type.
    private static FormCatalogFieldContext Context(BusinessFormCatalogField field, string? previousType = null,
        bool requiredTransition = false, string culture = "es") => new([field],
        new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>(),
        previousType is null ? new HashSet<Guid>() : new HashSet<Guid> { field.Id },
        requiredTransition ? new HashSet<Guid> { field.Id } : new HashSet<Guid>(),
        new Dictionary<Guid, BusinessFormCatalogField> { [field.Id] = previousType is null ? field : Field(previousType) with { Id = field.Id } },
        culture);

    private static string? StoredInput(BusinessFormCatalogItem item, BusinessFormCatalogField field) =>
        FormCatalogFieldPolicy.Input(FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson)[field.Id.ToString("D")]);

    private static void StoreText(BusinessFormCatalog catalog, BusinessFormCatalogItem item, BusinessFormCatalogField field, string value) =>
        Assert.True(catalog.SetManualItemFields(item, JsonSerializer.Serialize(new Dictionary<string, string> { [field.Id.ToString("D")] = value })).IsSuccess);

    private sealed record ZoneScenario(BusinessFormCatalog Catalog, BusinessFormCatalogField Field, FormCatalogStoredItemRow North,
        FormCatalogStoredItemRow South, FormCatalogFieldContext Context)
    {
        public string Key => Field.Id.ToString("D");
    }

    private static ZoneScenario Zones()
    {
        var field = Field("catalog") with { Key = "zone" };
        var sourceId = field.SourceCatalogId!.Value;
        var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Proveedores" }, new Dictionary<string, string>());
        var north = new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "N", "Norte", null, null, null, 1, false);
        var south = new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "S", "Sur", null, null, null, 2, true);
        var context = new FormCatalogFieldContext([field], new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> { [sourceId] = [north, south] },
            new HashSet<Guid>(), new HashSet<Guid>(), new Dictionary<Guid, BusinessFormCatalogField> { [field.Id] = field }, "es");
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
