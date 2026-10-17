using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormScalarPrefillPolicyTests
{
    private static readonly IReadOnlyDictionary<string, string> Texts = new Dictionary<string, string> { ["es"] = "Campo", ["en"] = "Field" };
    [Theory]
    [InlineData("shortText", "Visita al local", "Visita al local")]
    [InlineData("longText", "Primera línea\nOtra línea", "Primera línea\nOtra línea")]
    [InlineData("decimal", "0", "0")]
    [InlineData("decimal", "-12.50", "-12.5")]
    [InlineData("decimal", "+00012.500", "12.5")]
    [InlineData("date", "2026-10-17", "2026-10-17")]
    [InlineData("boolean", "false", "false")]
    [InlineData("boolean", "True", "true")]
    public void Each_scalar_type_is_normalized_without_inventing_a_value(string type, string value, string expected)
    {
        var result = FormScalarPrefillPolicy.Normalize(Question(type), value, []);
        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value);
    }
    [Theory]
    [InlineData("decimal", "1e3")]
    [InlineData("decimal", "1,2")]
    [InlineData("decimal", "79228162514264337593543950336")]
    [InlineData("decimal", "0.12345678901234567890123456789")]
    [InlineData("date", "2026-02-30")]
    [InlineData("date", "17/10/2026")]
    [InlineData("boolean", "0")]
    [InlineData("photo", "12345")]
    [InlineData("document", "12345")]
    [InlineData("shortText", "")]
    [InlineData("shortText", "bad\0text")]
    public void Invalid_scalar_values_and_files_are_rejected(string type, string value) =>
        Assert.True(FormScalarPrefillPolicy.Normalize(Question(type), value, []).IsFailure);
    [Fact]
    public void Text_limits_and_unpaired_surrogates_are_checked_before_offering()
    {
        Assert.True(FormScalarPrefillPolicy.Normalize(Question("shortText"), new string('a', 80), []).IsSuccess);
        Assert.True(FormScalarPrefillPolicy.Normalize(Question("shortText"), new string('a', 81), []).IsFailure);
        Assert.True(FormScalarPrefillPolicy.Normalize(Question("longText"), new string((char)0xD800, 1), []).IsFailure);
    }
    [Fact]
    public void A_manual_choice_must_be_in_the_published_options()
    {
        var option = Guid.CreateVersion7();
        var question = Question("choice") with { Options = [new(option, Texts)] };
        var result = FormScalarPrefillPolicy.Normalize(question, option.ToString("D").ToUpperInvariant(), []);
        Assert.True(result.IsSuccess);
        Assert.Equal(option.ToString("D"), result.Value);
        Assert.True(FormScalarPrefillPolicy.Normalize(question, Guid.CreateVersion7().ToString("D"), []).IsFailure);
    }
    [Fact]
    public void A_catalog_choice_must_belong_to_the_same_field_and_offer()
    {
        var id = Guid.CreateVersion7(); var catalogId = Guid.CreateVersion7();
        var question = Question("choice") with { SourceCatalogId = catalogId };
        StructuredFormCatalogOffer offer = new(question.FieldId, question.Key, "Campo", catalogId, 1, "Manual", "Personas", [new(id, "Persona", null)]);
        Assert.True(FormScalarPrefillPolicy.Normalize(question, id.ToString("D"), [offer]).IsSuccess);
        Assert.True(FormScalarPrefillPolicy.Normalize(question, id.ToString("D"), [offer with { FieldId = Guid.CreateVersion7() }]).IsFailure);
        Assert.True(FormScalarPrefillPolicy.Normalize(question, id.ToString("D"), [offer with { CatalogId = Guid.CreateVersion7() }]).IsFailure);
        Assert.True(FormScalarPrefillPolicy.Normalize(question, Guid.CreateVersion7().ToString("D"), [offer]).IsFailure);
    }
    private static StructuredFormQuestion Question(string type)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Texts, null, true, type == "shortText" ? 80 : type == "longText" ? 600 : null, null);
    }
}
