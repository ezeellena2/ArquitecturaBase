using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogImportCultureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    [Theory]
    [InlineData("en", BusinessFormCatalogValueType.Integer, "1,500", "1500")]
    [InlineData("en", BusinessFormCatalogValueType.Decimal, "1,500", "1500")]
    [InlineData("en", BusinessFormCatalogValueType.Decimal, "1,500.25", "1500.25")]
    [InlineData("es", BusinessFormCatalogValueType.Integer, "1.500.000", "1500000")]
    [InlineData("es", BusinessFormCatalogValueType.Decimal, "1.500.000,25", "1500000.25")]
    [InlineData("es", BusinessFormCatalogValueType.Decimal, "1.500,25", "1500.25")]
    [InlineData("en", BusinessFormCatalogValueType.Date, "3/4/2026", "2026-03-04")]
    [InlineData("es", BusinessFormCatalogValueType.Date, "3/4/2026", "2026-04-03")]
    [InlineData("es", BusinessFormCatalogValueType.Date, "2026-03-04", "2026-03-04")]
    [InlineData("en", BusinessFormCatalogValueType.Date, "2026-03-04", "2026-03-04")]
    public void Human_values_follow_the_import_culture(string culture, BusinessFormCatalogValueType type,
        string input, string expected)
    {
        var result = Build(type, culture, "Value", new(input));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues);
        Assert.Equal(expected, Assert.Single(result.Value.Items).Value);
    }

    [Theory]
    [InlineData("1.500")]
    [InlineData("12.345")]
    [InlineData("0.001")]
    public void A_decimal_with_different_local_and_canonical_meanings_requires_an_explicit_format(string input)
    {
        var result = Build(BusinessFormCatalogValueType.Decimal, "es", "Valor", new(input));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal("Forms.CatalogImport.ValueAmbiguous", Assert.Single(result.Value.Preview.Issues).Code);
    }

    [Theory]
    [InlineData("0.001")]
    [InlineData("12.345")]
    [InlineData("0.1234567890123456789012345678")]
    public void An_explicit_canonical_header_preserves_every_decimal_digit_in_spanish(string input)
    {
        var result = Build(BusinessFormCatalogValueType.Decimal, "es", "Valor canónico (decimal)", new(input));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues);
        Assert.Equal(input, Assert.Single(result.Value.Items).Value);
    }

    [Theory]
    [InlineData("es", "12.345")]
    [InlineData("en", "12.345")]
    public void Typed_spreadsheet_numbers_are_canonical_independently_of_the_ui_culture(string culture, string input)
    {
        var result = Build(BusinessFormCatalogValueType.Decimal, culture, "Value", new(input, IsCanonicalValue: true));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Preview.Issues);
        Assert.Equal(input, Assert.Single(result.Value.Items).Value);
    }

    [Fact]
    public void The_canonical_header_must_name_the_current_catalog_type()
    {
        var result = Build(BusinessFormCatalogValueType.Decimal, "es", "Valor canónico (fecha)", new("12.345"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
        Assert.Equal("Forms.Catalog.ItemValueInvalid", Assert.Single(result.Value.Preview.Issues).Code);
    }

    [Fact]
    public void Human_and_canonical_value_headers_are_the_same_logical_column()
    {
        var sheet = Sheet([new("Code"), new("Text"), new("Value"), new("Canonical value (decimal)")],
            [new("A"), new("Option"), new("1.2"), new("1.2")]);

        var result = FormCatalogImportPolicy.Build(new(Guid.NewGuid(), 1, BusinessFormCatalogValueType.Decimal, []),
            sheet, FormCatalogImportMode.Append, "en");

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.DuplicateColumn", result.Error.Code);
        Assert.Equal("value", result.Error.Metadata!["column"]);
    }

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Text, null)]
    [InlineData(BusinessFormCatalogValueType.Decimal, "12.34")]
    public void Added_rows_serialize_the_resolved_value_including_null(BusinessFormCatalogValueType type, string? input)
    {
        var result = Build(type, "en", "Value", new(input));
        Assert.True(result.IsSuccess);
        var json = JsonSerializer.SerializeToElement(result.Value.Preview, JsonOptions);

        Assert.Equal(input, json.GetProperty("added")[0].GetProperty("value").GetString());
    }

    private static ArquitecturaBase.Domain.Results.Result<FormCatalogImportPlan> Build(BusinessFormCatalogValueType type,
        string culture, string valueHeader, FormSpreadsheetCell value) =>
        FormCatalogImportPolicy.Build(new(Guid.NewGuid(), 1, type, []),
            Sheet([new("Code"), new("Text"), new(valueHeader)], [new("A"), new("Option"), value]),
            FormCatalogImportMode.Append, culture);

    private static FormSpreadsheet Sheet(FormSpreadsheetCell[] header, FormSpreadsheetCell[] row) =>
        new("Options", new('c', 64), [new(1, header), new(2, row)]);
}
