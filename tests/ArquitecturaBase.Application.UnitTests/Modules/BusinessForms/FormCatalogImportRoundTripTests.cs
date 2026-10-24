using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogImportRoundTripTests
{
    private static readonly Guid CatalogId = Guid.Parse("01990000-0000-7000-8000-000000000071");
    private static readonly string Hash = new('c', 64);
    private static FormSpreadsheet Sheet(IEnumerable<string?> header, params string?[][] rows) =>
        new("Options", Hash, new[] { header.ToArray() }.Concat(rows).Select((cells, index) =>
            new FormSpreadsheetRow(index + 1, cells.Select(text => new FormSpreadsheetCell(text)).ToArray())).ToArray());

    [Theory]
    [InlineData("es", BusinessFormCatalogValueType.Text)]
    [InlineData("en", BusinessFormCatalogValueType.Text)]
    [InlineData("es", BusinessFormCatalogValueType.Decimal)]
    [InlineData("en", BusinessFormCatalogValueType.Date)]
    public void The_unmodified_template_rule_row_is_not_an_option(string culture, BusinessFormCatalogValueType type)
    {
        var columns = FormCatalogImportTemplate.Columns(type, culture, "es");
        var source = new FormCatalogImportSource(CatalogId, 1, type, "es", []);
        var result = FormCatalogImportPolicy.Build(source, Sheet(columns.Select(column => column.Header),
            columns.Select(column => (string?)column.Rule).ToArray()), FormCatalogImportMode.Replace, culture);
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Preview.RowsRead);
        Assert.Equal(0, result.Value.Preview.Counts.ErrorRows);
        Assert.Empty(result.Value.Items);
        Assert.True(result.Value.Preview.Confirmations.EmptySheet);
    }

    [Fact]
    public void A_real_invalid_row_is_not_mistaken_for_template_instructions()
    {
        var result = FormCatalogImportPolicy.Build(new(CatalogId, 1, BusinessFormCatalogValueType.Text, "es", []),
            Sheet(["Code", "Text"], ["", "A real option"]), FormCatalogImportMode.Append, "en");
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Preview.RowsRead);
        Assert.Equal(1, result.Value.Preview.Counts.ErrorRows);
        Assert.Equal("code", Assert.Single(result.Value.Preview.Issues).Column);
    }

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Text, null)]
    [InlineData(BusinessFormCatalogValueType.Integer, "123456789012345678")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "0.1234567890123456789012345678")]
    [InlineData(BusinessFormCatalogValueType.Date, "2026-10-22")]
    public void Exported_canonical_values_and_all_languages_round_trip_without_mutations(BusinessFormCatalogValueType type, string? value)
    {
        var columns = FormCatalogImportTemplate.Columns(type, "en", "es");
        var item = new FormCatalogStoredItemRow(Guid.NewGuid(), CatalogId, "A", "Original", "English", "Detalle", "Detail", 17, false, value);
        var row = new Dictionary<string, string?> { ["code"]="A", ["text"]="Original", ["textEn"]="English", ["detail"]="Detalle", ["detailEn"]="Detail", ["active"]="No", ["value"]=value };
        var result = FormCatalogImportPolicy.Build(new(CatalogId, 9, type, "es", [item]),
            Sheet(columns.Select(column=>column.Header), columns.Select(column=>row[column.Key]).ToArray()), FormCatalogImportMode.Replace, "en");
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Preview.Counts.Unchanged);
        Assert.Equal(0, result.Value.Preview.Counts.Changed);
        Assert.Empty(result.Value.Items);
        Assert.Empty(result.Value.Preview.Issues);
        Assert.False(item.IsActive);
        Assert.Equal(17, item.Position);
        Assert.Equal(value, item.Value);
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void An_English_original_catalog_round_trips_without_changes(string culture)
    {
        var columns = FormCatalogImportTemplate.Columns(BusinessFormCatalogValueType.Text, culture, "en");
        var translated = new FormCatalogStoredItemRow(Guid.NewGuid(), CatalogId, "N", "Norte", "North", "Detalle norte", "North detail", 0, true);
        var englishOnly = new FormCatalogStoredItemRow(Guid.NewGuid(), CatalogId, "E", null, "East", null, null, 1, false);
        string?[] Row(FormCatalogStoredItemRow item) => columns.Select(column => column.Key switch
        {
            "code" => item.Code, "text" => item.TextEn, "textEs" => item.TextEs, "detail" => item.DetailEn, "detailEs" => item.DetailEs,
            "active" => item.IsActive ? "Yes" : "No", _ => null,
        }).ToArray();

        var result = FormCatalogImportPolicy.Build(new(CatalogId, 4, BusinessFormCatalogValueType.Text, "en", [translated, englishOnly]),
            Sheet(columns.Select(column => column.Header), Row(translated), Row(englishOnly)), FormCatalogImportMode.Replace, culture);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues);
        Assert.Equal(2, result.Value.Preview.Counts.Unchanged);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public void An_invalid_typed_value_preserves_the_entire_matching_inactive_item()
    {
        var item = new FormCatalogStoredItemRow(Guid.NewGuid(), CatalogId, "A", "Original", "English", "Old", "Old", 17, false, "12.5");
        var result = FormCatalogImportPolicy.Build(new(CatalogId, 9, BusinessFormCatalogValueType.Decimal, "es", [item]),
            Sheet(["Code", "Text", "Value"], ["A", "Changed", "1,2,3"]), FormCatalogImportMode.Replace, "es");
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal(1, result.Value.Preview.Counts.ErrorRows);
        Assert.Equal("value", Assert.Single(result.Value.Preview.Issues).Column);
        Assert.Equal("12.5", item.Value);
        Assert.False(item.IsActive);
    }

    [Theory]
    [InlineData("code", false)]
    [InlineData("text", false)]
    [InlineData("code", true)]
    public void Fatal_header_errors_identify_the_logical_column(string column, bool duplicate)
    {
        string[] header = duplicate ? ["Code", "Texto", "Codigo"] : column == "code" ? ["Text"] : ["Code"];
        var result = FormCatalogImportPolicy.Build(new(CatalogId, 1, BusinessFormCatalogValueType.Text, "es", []), Sheet(header), FormCatalogImportMode.Replace, "es");
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error.Metadata);
        Assert.Equal(column, result.Error.Metadata["column"]);
    }
}
