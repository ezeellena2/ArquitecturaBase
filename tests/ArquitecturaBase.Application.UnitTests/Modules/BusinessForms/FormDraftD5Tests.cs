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
    public void Publication_reports_each_missing_text_with_its_language_and_stable_identity()
    {
        var fieldId = Guid.CreateVersion7(); var choiceId = Guid.CreateVersion7();
        var original = new Dictionary<string, string> { ["en"] = "Original" };
        var definition = new FormDefinitionResponse(Guid.CreateVersion7(), original, "FORM", false, 1,
            new(1, "en", original, [new(fieldId, "field_" + fieldId.ToString("N"), "choice", original, original, false, null, [new(choiceId, original)])]));
        var issues = FormPublicationPolicy.MissingTranslations(definition);
        Assert.Equal(new[] { "name.es", "draft.submitLabel.es", "draft.fields[0].label.es", "draft.fields[0].help.es", "draft.fields[0].options[0].label.es" }, issues.Select(issue => issue.Path));
        Assert.All(issues, issue => Assert.Equal("es", issue.Culture));
        Assert.Equal(fieldId, issues[2].FieldId); Assert.Equal(choiceId, issues[4].ChoiceId);
        Assert.All(issues, issue => Assert.Null(issue.ChannelId));
    }
    [Fact]
    public void Publication_accepts_complete_texts_without_inventing_optional_help_or_secondary_duplicate_rules()
    {
        var text = new Dictionary<string, string> { ["es"] = "Texto", ["en"] = "Text" };
        var fieldId = Guid.CreateVersion7();
        var definition = new FormDefinitionResponse(Guid.CreateVersion7(), text, "FORM", false, 1,
            new(1, "es", text, [new(fieldId, "field_" + fieldId.ToString("N"), "shortText", text, null, false, 80, null)]));
        Assert.Empty(FormPublicationPolicy.MissingTranslations(definition));
        var options = Request(text).Draft.Fields[0].Options!;
        var request = Request(text) with { Draft = Request(text).Draft with { Fields = [Request(text).Draft.Fields[0] with {
            Options = [options[0], new("b", null, new Dictionary<string, string> { ["es"] = "Otra", ["en"] = "Text" })] }] } };
        Assert.True(new SaveFormDraftRequestValidator().Validate(request).IsValid);
    }
    [Theory]
    [InlineData("name")]
    [InlineData("submitLabel")]
    [InlineData("label")]
    [InlineData("help")]
    public void Draft_text_rejects_controls_and_invisible_only_values_at_the_field_path(string target)
    {
        var request = Request(new Dictionary<string, string> { ["es"] = "Texto" });
        request = target switch
        {
            "name" => request with { Name = new Dictionary<string, string> { ["es"] = "\0" } },
            "submitLabel" => request with { Draft = request.Draft with { SubmitLabel = new Dictionary<string, string> { ["es"] = "Texto\0" } } },
            "label" => request with { Draft = request.Draft with { Fields = [request.Draft.Fields[0] with { Label = new Dictionary<string, string> { ["es"] = "\u200B" } }] } },
            _ => request with { Draft = request.Draft with { Fields = [request.Draft.Fields[0] with { Help = new Dictionary<string, string?> { ["es"] = "\u200B" } }] } },
        };
        var errors = new SaveFormDraftRequestValidator().Validate(request).Errors;
        Assert.Contains(errors, error => error.PropertyName.Contains(target, StringComparison.Ordinal));
    }
    [Fact]
    public void Help_keeps_line_breaks_and_tabs_while_rejecting_other_controls()
    {
        var request = Request(new Dictionary<string, string> { ["es"] = "Texto" });
        var field = request.Draft.Fields[0];
        var validator = new SaveFormDraftRequestValidator();
        Assert.True(validator.Validate(request with { Draft = request.Draft with { Fields = [field with {
            Help = new Dictionary<string, string?> { ["es"] = "Primera línea\n\tDetalle" } }] } }).IsValid);
        Assert.False(validator.Validate(request with { Draft = request.Draft with { Fields = [field with {
            Help = new Dictionary<string, string?> { ["es"] = "Detalle\0" } }] } }).IsValid);
    }
    private static SaveFormDraftRequest Request(IReadOnlyDictionary<string, string> texts) => new(Guid.NewGuid(), texts, "FORM", new(1, "es", texts,
        [new("a", null, null, "choice", texts, new Dictionary<string, string?> { ["es"] = "Ayuda" }, false, null, [new("o", null, texts)])]));
}
