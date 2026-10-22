using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogValuePolicyTests
{
    [Theory]
    [InlineData(BusinessFormCatalogValueType.Integer, "0", "0")]
    [InlineData(BusinessFormCatalogValueType.Integer, "-1500", "-1500")]
    [InlineData(BusinessFormCatalogValueType.Integer, "999999999999999999", "999999999999999999")]
    [InlineData(BusinessFormCatalogValueType.Integer, "-999999999999999999", "-999999999999999999")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1500.00", "1500")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "-0.000", "0")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "0.0000000000000000000000000001", "0.0000000000000000000000000001")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData(BusinessFormCatalogValueType.Date, "2024-02-29", "2024-02-29")]
    public void Typed_values_are_canonical_without_rounding(BusinessFormCatalogValueType type, string input, string expected)
    {
        var result = FormCatalogValuePolicy.Normalize(type, input);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Integer, "1000000000000000000")]
    [InlineData(BusinessFormCatalogValueType.Integer, "1.0")]
    [InlineData(BusinessFormCatalogValueType.Integer, "1e2")]
    [InlineData(BusinessFormCatalogValueType.Integer, "1,500")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "79228162514264337593543950336")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "0.00000000000000000000000000001")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1e2")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1.500,50")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "NaN")]
    [InlineData(BusinessFormCatalogValueType.Date, "2023-02-29")]
    [InlineData(BusinessFormCatalogValueType.Date, "21/10/2026")]
    [InlineData(BusinessFormCatalogValueType.Date, "2026-10-21T00:00:00Z")]
    [InlineData((BusinessFormCatalogValueType)999, "1")]
    public void Invalid_or_lossy_values_fail_with_the_catalog_error(BusinessFormCatalogValueType type, string input)
    {
        var result = FormCatalogValuePolicy.Normalize(type, input);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Catalog.ItemValueInvalid", result.Error.Code);
    }

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Integer, null)]
    [InlineData(BusinessFormCatalogValueType.Integer, "")]
    [InlineData(BusinessFormCatalogValueType.Decimal, " ")]
    [InlineData(BusinessFormCatalogValueType.Date, null)]
    public void Every_typed_option_requires_a_value(BusinessFormCatalogValueType type, string? input)
    {
        var result = FormCatalogValuePolicy.Normalize(type, input);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Catalog.ItemValueRequired", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Text_catalogs_keep_the_original_absent_value(string? input)
    {
        var result = FormCatalogValuePolicy.Normalize(BusinessFormCatalogValueType.Text, input);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }
}
