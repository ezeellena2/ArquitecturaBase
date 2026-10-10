using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormDraftD5Tests
{
    [Fact]
    public void Secondary_texts_can_be_empty_while_original_texts_remain_required()
    {
        var texts = new Dictionary<string, string> { ["es"] = "Texto", ["en"] = "" };
        var request = Request(texts);
        var validator = new SaveFormDraftRequestValidator();
        Assert.True(validator.Validate(request).IsValid);
        var noOriginal = request with { Draft = request.Draft with { SubmitLabel = new Dictionary<string, string> { ["es"] = "", ["en"] = "Submit" } } };
        Assert.False(validator.Validate(noOriginal).IsValid);
    }
    [Theory]
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public void Name_requires_the_original_language_and_rejects_null_unknown_and_oversized_values(string original, string secondary)
    {
        var name = new Dictionary<string, string> { [original] = "Name", [secondary] = "" };
        var request = Request(name) with { Draft = Request(name).Draft with { Culture = original } };
        var validator = new SaveFormDraftRequestValidator();
        Assert.True(validator.Validate(request).IsValid);
        foreach (var invalid in new Dictionary<string, string>[] {
            new() { [secondary] = "Name" },
            new() { [original] = " " },
            new() { [original] = "Name", [secondary] = null! },
            new() { [original] = "Name", ["fr"] = "Nom" },
            new() { [original] = "Name", [secondary] = new string('x', 121) } })
            Assert.Contains(validator.Validate(request with { Name = invalid }).Errors, error => error.PropertyName.StartsWith("name.", StringComparison.Ordinal));
    }
    [Fact]
    public void Normalization_preserves_explicit_pending_translations_and_does_not_fill_missing_keys()
    {
        var texts = new Dictionary<string, string> { ["es"] = "Texto", ["en"] = "   " };
        var normalized = FormDraftPolicy.Normalize(Request(texts) with { Name = new Dictionary<string, string> { ["es"] = " Nombre ", ["en"] = " " } });
        Assert.Equal("Nombre", normalized.Name["es"]); Assert.Equal("", normalized.Name["en"]);
        Assert.Equal("", normalized.Draft.SubmitLabel!["en"]); Assert.Equal("", normalized.Draft.Fields[0].Label["en"]);
        Assert.Equal("", normalized.Draft.Fields[0].Options![0].Label["en"]);
        var missing = FormDraftPolicy.Normalize(Request(new Dictionary<string, string> { ["es"] = "Texto" }));
        Assert.False(missing.Name.ContainsKey("en")); Assert.False(missing.Draft.Fields[0].Label.ContainsKey("en"));
    }
    [Fact]
    public void Secondary_duplicate_option_text_does_not_add_an_unapproved_uniqueness_rule()
    {
        var text = new Dictionary<string, string> { ["es"] = "Texto", ["en"] = "Text" };
        var options = Request(text).Draft.Fields[0].Options!;
        var request = Request(text) with { Draft = Request(text).Draft with { Fields = [Request(text).Draft.Fields[0] with {
            Options = [options[0], new("b", null, new Dictionary<string, string> { ["es"] = "Otra", ["en"] = "Text" })] }] } };
        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
    }
    private static SaveFormDraftRequest Request(IReadOnlyDictionary<string, string> texts) => new(Guid.NewGuid(), texts, "FORM", new(1, "es", texts,
        [new("a", null, null, "choice", texts, new Dictionary<string, string?> { ["es"] = "Ayuda" }, false, null, [new("o", null, texts)])]));
}