using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldPolicyTests
{
    [Theory]
    [InlineData("text", "  abc  ", "\"abc\"")]
    [InlineData("integer", "123456789012345678", "123456789012345678")]
    [InlineData("decimal", "001500.5000", "\"1500.5\"")]
    [InlineData("date", "2026-10-22", "\"2026-10-22\"")]
    [InlineData("boolean", "false", "false")]
    public void Values_have_one_typed_json_representation(string type, string input, string expected)
    {
        var result = FormCatalogFieldPolicy.Normalize(Field(type), input);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value!.Value.GetRawText());
    }

    [Theory]
    [InlineData("integer", "1.2")]
    [InlineData("integer", "1234567890123456789")]
    [InlineData("decimal", "1,2")]
    [InlineData("date", "2026-02-30")]
    [InlineData("boolean", "perhaps")]
    [InlineData("catalog", "00000000-0000-0000-0000-000000000000")]
    public void Invalid_values_are_rejected_without_coercing_them(string type, string input)
    {
        Assert.Equal("Forms.Catalog.FieldValueInvalid", FormCatalogFieldPolicy.Normalize(Field(type), input).Error.Code);
    }

    [Fact]
    public void Empty_optional_values_are_absent_and_required_values_cannot_be_empty()
    {
        Assert.Null(FormCatalogFieldPolicy.Normalize(Field("text"), "  ").Value);
        Assert.Equal("Forms.Catalog.FieldValueRequired", FormCatalogFieldPolicy.Normalize(Field("text") with { Required = true }, null).Error.Code);
    }

    [Fact]
    public void Numeric_date_and_text_bounds_are_checked_against_the_canonical_value()
    {
        Assert.Equal("Forms.Catalog.FieldValueInvalid", FormCatalogFieldPolicy.Normalize(Field("integer") with { Min = "10", Max = "20" }, "9").Error.Code);
        Assert.True(FormCatalogFieldPolicy.Normalize(Field("integer") with { Min = "10", Max = "20" }, "20").IsSuccess);
        Assert.Equal("Forms.Catalog.FieldValueInvalid", FormCatalogFieldPolicy.Normalize(Field("date") with { Min = "2026-10-22" }, "2026-10-21").Error.Code);
        Assert.Equal("Forms.Catalog.FieldValueInvalid", FormCatalogFieldPolicy.Normalize(Field("text") with { MaxLength = 3 }, "abcd").Error.Code);
    }

    private static BusinessFormCatalogField Field(string type) => new(Guid.CreateVersion7(), "field", new Dictionary<string, string> { ["es"] = "Campo" }, type,
        type == "catalog" ? Guid.CreateVersion7() : null, false, false, null, null, type == "text" ? 300 : null, "data", 0, true);
}
