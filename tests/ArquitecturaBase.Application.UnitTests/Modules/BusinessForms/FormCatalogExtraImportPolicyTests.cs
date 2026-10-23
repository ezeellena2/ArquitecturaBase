using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogExtraImportPolicyTests
{
    private static readonly Guid CatalogId = Guid.CreateVersion7();
    private static readonly string Hash = new('a', 64);

    [Theory]
    [InlineData("es", "amount", "1.500,50")]
    [InlineData("es", "Monto", "1.500,50")]
    [InlineData("en", "Amount", "1,500.50")]
    public void Extra_columns_accept_the_key_or_either_name_and_preview_the_normalized_value(string culture, string header, string input)
    {
        var field = Field("amount", "decimal", "Monto", "Amount");
        var result = FormCatalogImportPolicy.Build(Source([field]), Sheet(["Code", "Text", header], ["A", "Opción", input]), FormCatalogImportMode.Append, culture);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues);
        var added = Assert.Single(result.Value.Preview.Added);
        Assert.Equal("1500.5", added.Fields![field.Id.ToString("D")]);
        Assert.Equal("1500.5", Assert.Single(result.Value.Items).Fields![field.Id.ToString("D")]);
    }

    [Fact]
    public void Extra_columns_preserve_their_json_types_and_resolve_catalog_codes_or_texts()
    {
        var sourceId = Guid.CreateVersion7();
        var optionId = Guid.CreateVersion7();
        var fields = new[] { Field("days", "integer"), Field("amount", "decimal"), Field("date", "date"), Field("enabled", "boolean"),
            Field("zone", "catalog") with { SourceCatalogId = sourceId }, Field("note", "text") };
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>
        { [sourceId] = [new(optionId, sourceId, "CENTRAL", "Centro", "Central", null, null, 0, true)] };
        foreach (var selection in new[] { "central", "Centro", "Central" })
        {
            var result = FormCatalogImportPolicy.Build(Source(fields, sources: sources),
                Sheet(["Code", "Text", "days", "amount", "date", "enabled", "zone", "note"],
                    ["A", "Opción", "15", "0.25", "22/10/2026", "Sí", selection, "  Texto  "]), FormCatalogImportMode.Append, "es");
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value.Preview.Issues);
            var values = Assert.Single(result.Value.Preview.Added).Fields!;
            var number = FormCatalogFieldPolicy.Normalize(fields[0], values[fields[0].Id.ToString("D")]).Value!.Value;
            Assert.Equal(JsonValueKind.Number, number.ValueKind);
            Assert.Equal(15, number.GetInt32());
            Assert.Equal("0.25", values[fields[1].Id.ToString("D")]);
            Assert.Equal("2026-10-22", values[fields[2].Id.ToString("D")]);
            Assert.Equal("true", values[fields[3].Id.ToString("D")]);
            Assert.Equal(optionId.ToString("D"), values[fields[4].Id.ToString("D")]);
            Assert.Equal("Texto", values[fields[5].Id.ToString("D")]);
        }
    }

    [Theory]
    [InlineData("es", "Centro", "Sur")]
    [InlineData("en", "Central", "Sur")]
    public void Catalog_extra_values_preview_the_source_text_without_asking_the_browser_to_resolve_ids(string culture, string expectedBefore, string expectedAfter)
    {
        var sourceId = Guid.CreateVersion7();
        var beforeId = Guid.CreateVersion7();
        var afterId = Guid.CreateVersion7();
        var field = Field("zone", "catalog") with { SourceCatalogId = sourceId };
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>
        { [sourceId] = [new(beforeId, sourceId, "CENTRAL", "Centro", "Central", null, null, 1, true),
            new(afterId, sourceId, "SOUTH", "Sur", null, null, null, 2, true)] };
        var source = Source([field], [Item("A", "Original", Values((field.Id, beforeId.ToString("D"))))], sources);
        var result = FormCatalogImportPolicy.Build(source,
            Sheet(["Code", "Text", "zone"], ["A", "Original", "SOUTH"], ["B", "Nueva", "CENTRAL"]), FormCatalogImportMode.Append, culture);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues);
        Assert.Equal(expectedBefore, Assert.Single(result.Value.Preview.Added).FieldTexts![field.Id.ToString("D")]);
        var change = Assert.Single(Assert.Single(result.Value.Preview.Changed).Changes);
        Assert.Equal(beforeId.ToString("D"), change.Before);
        Assert.Equal(afterId.ToString("D"), change.After);
        Assert.Equal(expectedBefore, change.BeforeText);
        Assert.Equal(expectedAfter, change.AfterText);
    }

    [Fact]
    public void Missing_optional_columns_preserve_saved_extra_values_and_invalid_rows_preserve_the_whole_item()
    {
        var field = Field("amount", "decimal");
        var item = Item("A", "Original", Values((field.Id, "0.001")));
        var source = Source([field], [item]);
        var absent = FormCatalogImportPolicy.Build(source, Sheet(["Code", "Text"], ["A", "Original"]), FormCatalogImportMode.Replace, "es");
        Assert.True(absent.IsSuccess);
        Assert.Equal(1, absent.Value.Preview.Counts.Unchanged);
        Assert.Empty(absent.Value.Items);
        var invalid = FormCatalogImportPolicy.Build(source, Sheet(["Code", "Text", "amount"], ["A", "Cambio", "bad"]), FormCatalogImportMode.Replace, "es");
        Assert.True(invalid.IsSuccess);
        Assert.Empty(invalid.Value.Items);
        Assert.Equal(field.Id.ToString("D"), Assert.Single(invalid.Value.Preview.Issues).Column);
        Assert.Equal(0, invalid.Value.Preview.Counts.Deactivated);
        Assert.Equal(Values((field.Id, "0.001")), item.FieldValuesJson);
    }

    [Fact]
    public void Required_extra_values_are_checked_per_active_row_and_changes_identify_the_field()
    {
        var field = Field("note", "text") with { Required = true };
        var item = Item("A", "Original", Values((field.Id, "Antes")));
        var changed = FormCatalogImportPolicy.Build(Source([field], [item]), Sheet(["Code", "Text", "note"], ["A", "Original", "Después"]), FormCatalogImportMode.Append, "es");
        Assert.True(changed.IsSuccess);
        var change = Assert.Single(Assert.Single(changed.Value.Preview.Changed).Changes);
        Assert.Equal(field.Id.ToString("D"), change.Column);
        Assert.Equal("Antes", change.Before);
        Assert.Equal("Después", change.After);
        var missing = FormCatalogImportPolicy.Build(Source([field]), Sheet(["Code", "Text"], ["NEW", "Nueva"]), FormCatalogImportMode.Append, "es");
        Assert.Equal(1, missing.Value.Preview.Counts.ErrorRows);
        Assert.Equal("Forms.Catalog.FieldValueRequired", Assert.Single(missing.Value.Preview.Issues).Code);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Canonical_extra_headers_round_trip_exact_values_and_template_instructions_are_not_items(string culture)
    {
        var fields = new[] { Field("amount", "decimal"), Field("days", "integer"), Field("code", "text") };
        var item = Item("A", "Opción", Values((fields[0].Id, "0.001"), (fields[1].Id, 123456789012345678L), (fields[2].Id, "Propio")));
        var columns = FormCatalogImportTemplate.Columns(BusinessFormCatalogValueType.Text, culture, canonicalValue: true, fields: fields);
        var values = FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson);
        var row = columns.Select(column => column.Key switch
        {
            "code" => "A", "text" => "Opción", "active" => "Yes", "textEn" or "detail" or "detailEn" => null,
            _ => FormCatalogFieldPolicy.Input(values[column.Key]),
        }).ToArray();
        var result = FormCatalogImportPolicy.Build(Source(fields, [item]), Sheet(columns.Select(column => column.Header).ToArray(), row), FormCatalogImportMode.Replace, culture);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues);
        Assert.Equal(1, result.Value.Preview.Counts.Unchanged);
        Assert.Empty(result.Value.Items);
        var template = FormCatalogImportTemplate.Columns(BusinessFormCatalogValueType.Text, culture, fields: fields);
        Assert.Equal(9, template.Count);
        Assert.NotEqual(template[0].Header, template[8].Header);
        var instructions = FormCatalogImportPolicy.Build(Source(fields), Sheet(template.Select(column => column.Header).ToArray(),
            template.Select(column => (string?)column.Rule).ToArray()), FormCatalogImportMode.Append, culture);
        Assert.True(instructions.IsSuccess);
        Assert.Equal(0, instructions.Value.Preview.RowsRead);
        Assert.Empty(instructions.Value.Items);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Reimporting_a_saved_inactive_source_option_is_an_unchanged_row(string culture)
    {
        var (field, sources, north, _) = Zones();
        var item = Item("A", "Opción", Values((field.Id, north.Id.ToString("D"))));
        var columns = FormCatalogImportTemplate.Columns(BusinessFormCatalogValueType.Text, culture, canonicalValue: true, fields: [field]);
        var row = columns.Select(column => column.Key switch
        {
            "code" => "A", "text" => "Opción", "active" => "Yes", "textEn" or "detail" or "detailEn" => null, _ => north.Id.ToString("D"),
        }).ToArray();
        var result = FormCatalogImportPolicy.Build(Source([field], [item], sources), Sheet(columns.Select(column => column.Header).ToArray(), row),
            FormCatalogImportMode.Replace, culture);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues.Select(issue => issue.Code));
        Assert.Equal(1, result.Value.Preview.Counts.Unchanged);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public void An_inactive_source_option_is_still_a_row_error_as_a_new_or_changed_value()
    {
        var (field, sources, north, south) = Zones();
        var source = Source([field], [Item("A", "Opción", Values((field.Id, south.Id.ToString("D"))))], sources);
        foreach (var sheet in new[] { Sheet(["Code", "Text", "zone"], ["A", "Opción", "NORTE"]), Sheet(["Code", "Text", "zone"], ["B", "Nueva", north.Id.ToString("D")]) })
        {
            var result = FormCatalogImportPolicy.Build(source, sheet, FormCatalogImportMode.Append, "es");
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value.Items);
            var issue = Assert.Single(result.Value.Preview.Issues);
            Assert.Equal(field.Id.ToString("D"), issue.Column);
            Assert.Equal("Forms.Catalog.FieldValueInvalid", issue.Code);
        }
    }

    [Fact]
    public void An_unknown_or_ambiguous_catalog_selection_is_a_row_error_and_not_a_silent_blank_value()
    {
        var sourceId = Guid.CreateVersion7();
        var field = Field("zone", "catalog") with { SourceCatalogId = sourceId };
        var options = new[] { new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "A", "Misma", null, null, null, 0, true),
            new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "B", "Misma", null, null, null, 1, true) };
        foreach (var value in new[] { "Misma", "Missing" })
        {
            var result = FormCatalogImportPolicy.Build(Source([field], sources: new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> { [sourceId] = options }),
                Sheet(["Code", "Text", "zone"], ["NEW", "Nueva", value]), FormCatalogImportMode.Append, "es");
            Assert.True(result.IsSuccess);
            Assert.Empty(result.Value.Items);
            Assert.Equal(field.Id.ToString("D"), Assert.Single(result.Value.Preview.Issues).Column);
        }
    }

    [Fact]
    public void Unique_extra_values_allow_swaps_but_reject_duplicates_against_saved_or_new_active_options()
    {
        var field = Field("tax_id", "text") with { Unique = true };
        var first = Item("A", "Primera", Values((field.Id, "one")));
        var second = Item("B", "Segunda", Values((field.Id, "two")));
        var source = Source([field], [first, second]);
        var swap = FormCatalogImportPolicy.Build(source,
            Sheet(["Code", "Text", "tax_id"], ["A", "Primera", "two"], ["B", "Segunda", "one"]), FormCatalogImportMode.Append, "en");
        Assert.True(swap.IsSuccess);
        Assert.Empty(swap.Value.Preview.Issues);
        Assert.Equal(2, swap.Value.Items.Count);
        var duplicate = FormCatalogImportPolicy.Build(source,
            Sheet(["Code", "Text", "tax_id"], ["C", "Tercera", "one"], ["D", "Cuarta", "one"]), FormCatalogImportMode.Append, "en");
        Assert.True(duplicate.IsSuccess);
        Assert.Empty(duplicate.Value.Items);
        Assert.Equal(2, duplicate.Value.Preview.Counts.ErrorRows);
        Assert.All(duplicate.Value.Preview.Issues, issue => Assert.Equal(field.Id.ToString("D"), issue.Column));
    }

    [Fact]
    public void A_blank_header_never_matches_a_field_even_when_a_name_translation_is_blank()
    {
        var field = Field("note", "text", "Nota", "");
        var source = Source([field], [Item("A", "Original", Values((field.Id, "Antes")))]);
        var single = FormCatalogImportPolicy.Build(source, Sheet(["Code", "Text", ""], ["A", "Original", "Basura"]), FormCatalogImportMode.Append, "es");
        Assert.True(single.IsSuccess);
        Assert.Empty(single.Value.Preview.Issues);
        Assert.Equal(0, single.Value.Preview.Counts.Changed);
        Assert.Equal(1, single.Value.Preview.Counts.Unchanged);
        Assert.Empty(single.Value.Items);

        var both = Source([Field("first", "text", "Primero", ""), Field("second", "text", "Segundo", "")]);
        var blanks = FormCatalogImportPolicy.Build(both, Sheet(["Code", "Text", "", ""], ["NEW", "Nueva", "Uno", "Dos"]), FormCatalogImportMode.Append, "es");
        Assert.True(blanks.IsSuccess, blanks.IsFailure ? blanks.Error.Code : null);
        Assert.Empty(blanks.Value.Preview.Issues);
        Assert.All(Assert.Single(blanks.Value.Items).Fields!.Values, Assert.Null);
    }

    // A source with «Norte» deactivated and «Sur» active, and a catalog field that points to it.
    private static (BusinessFormCatalogField Field, Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> Sources,
        FormCatalogStoredItemRow North, FormCatalogStoredItemRow South) Zones()
    {
        var sourceId = Guid.CreateVersion7();
        var north = new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "NORTE", "Norte", "North", null, null, 0, false);
        var south = new FormCatalogStoredItemRow(Guid.CreateVersion7(), sourceId, "SUR", "Sur", "South", null, null, 1, true);
        return (Field("zone", "catalog") with { SourceCatalogId = sourceId }, new() { [sourceId] = [north, south] }, north, south);
    }

    private static FormCatalogImportSource Source(IReadOnlyList<BusinessFormCatalogField> fields, IReadOnlyList<FormCatalogStoredItemRow>? items = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>? sources = null) => new(CatalogId, 1, BusinessFormCatalogValueType.Text,
            items ?? [], Fields: fields, Sources: sources);
    private static BusinessFormCatalogField Field(string key, string type, string? es = null, string? en = null) => new(Guid.CreateVersion7(), key,
        new Dictionary<string, string> { ["es"] = es ?? key, ["en"] = en ?? key }, type, null, false, false, null, null, null, "data", 0, true);
    private static FormCatalogStoredItemRow Item(string code, string text, string fields) => new(Guid.CreateVersion7(), CatalogId, code, text, null,
        null, null, 0, true, FieldValuesJson: fields);
    private static string Values(params (Guid Id, object Value)[] values) => JsonSerializer.Serialize(values.ToDictionary(value => value.Id.ToString("D"), value => value.Value));
    private static FormSpreadsheet Sheet(string[] header, params string?[][] rows) => new("Extra.csv", Hash,
        new[] { header.Cast<string?>().ToArray() }.Concat(rows).Select((row, index) => new FormSpreadsheetRow(index + 1,
            row.Select(value => new FormSpreadsheetCell(value)).ToArray())).ToArray());
}
