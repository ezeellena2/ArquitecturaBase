using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormModuleTests
{
    [Fact]
    public void Registration_is_idempotent_and_contributes_the_form_capability_and_permissions()
    {
        using var provider = new ServiceCollection().AddBusinessFormsApplication().AddBusinessFormsApplication().BuildServiceProvider();
        Assert.Equal(new[] { "forms" }, Assert.Single(provider.GetServices<IClientCapabilitySource>()).InstalledFeatures);
        Assert.Equal(5, Assert.Single(provider.GetServices<IPermissionCatalogSource>()).Codes.Count);
    }
    [Fact]
    public void Defaults_and_empty_help_normalize_without_generating_a_second_translation()
    {
        var input = new SaveFormDraftRequest(Guid.NewGuid(), new Dictionary<string, string> { ["en"] = " Form " }, " ab ", new(1, "en", null,
            [new("a", null, null, "longText", new Dictionary<string, string> { ["en"] = "Question" }, new Dictionary<string, string?> { ["en"] = "" }, false, null, null)]));
        var result = FormDraftPolicy.Normalize(input);
        Assert.Equal("AB", result.Prefix); Assert.Equal("Form", result.Name["en"]); Assert.Equal(600, result.Draft.Fields[0].MaxLength);
        Assert.Equal(new[] { "en" }, result.Draft.SubmitLabel!.Keys); Assert.Equal("Submit", result.Draft.SubmitLabel["en"]); Assert.Null(result.Draft.Fields[0].Help!["en"]);
    }
    [Theory]
    [InlineData("shortText")]
    [InlineData("longText")]
    [InlineData("decimal")]
    [InlineData("date")]
    [InlineData("boolean")]
    public void Manual_scalar_types_validate_and_group_does_not(string type)
    {
        var input = new SaveFormDraftRequest(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Form" }, "FORM", new(1, "es", null,
            [new("a", null, null, type, new Dictionary<string, string> { ["es"] = "Pregunta" }, null, false, null, null)]));
        var validator = new SaveFormDraftRequestValidator(); Assert.True(validator.Validate(input).IsValid);
        Assert.False(validator.Validate(input with { Draft = input.Draft with { Fields = [input.Draft.Fields[0] with { Type = "group" }] } }).IsValid);
    }
}
