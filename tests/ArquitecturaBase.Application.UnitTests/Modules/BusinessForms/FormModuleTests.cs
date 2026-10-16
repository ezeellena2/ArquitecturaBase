using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormModuleTests
{
    [Fact]
    public void Registration_is_idempotent_and_contributes_the_form_capability_and_permissions()
    {
        using var provider = new ServiceCollection().AddSingleton<ArquitecturaBase.Application.Interfaces.Integrations.Files.IPrivateFileStore>(new TestFeatures.Files.MemoryPrivateFileStore(false)).AddBusinessFormsApplication().AddBusinessFormsApplication().BuildServiceProvider();
        Assert.Equal(new[] { "forms" }, Assert.Single(provider.GetServices<IClientCapabilitySource>()).InstalledFeatures);
        Assert.Equal(8, Assert.Single(provider.GetServices<IPermissionCatalogSource>()).Codes.Count);
        Assert.Contains(BusinessFormsPermissions.RecordsExport, Assert.Single(provider.GetServices<IPermissionCatalogSource>()).Codes);
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

    [Fact]
    public void Catalog_choice_requires_one_source_and_replaces_confirmed_identity_when_source_changes()
    {
        var validator = new SaveFormDraftRequestValidator();
        var previousFieldId = Guid.NewGuid();
        var catalogId = Guid.NewGuid();
        var label = new Dictionary<string, string> { ["es"] = "Responsable", ["en"] = "Assignee" };
        var option = new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Uno", ["en"] = "One" });
        var previousField = new CanonicalFormField(previousFieldId, "field_" + previousFieldId.ToString("N"), "choice", label, null,
            true, null, [option]);
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, "Form", FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = "Form", ["en"] = "Form" }),
            "FORM", "es", FormDraftPolicy.Serialize(new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" }),
            FormDraftPolicy.Serialize(new FormQuestionSchema(1, [previousField])), 1, true).IsSuccess);
        var field = new FormFieldInput("choice", previousFieldId, null, "choice", label, null, true, null, null,
            new FormQuestionSource(catalogId));
        var request = new SaveFormDraftRequest(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Form", ["en"] = "Form" }, "FORM",
            new(1, "es", new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" }, [field]), 1);

        Assert.True(validator.Validate(request).IsValid);
        Assert.True(FormDraftPolicy.Resolve(request, definition, FormRetiredIdentities.Empty).IsFailure);

        var replacement = FormDraftPolicy.Resolve(request with
        {
            Draft = request.Draft with { Fields = [field with { FieldId = null, ReplacesFieldId = previousFieldId }] },
        }, definition, FormRetiredIdentities.Empty);
        Assert.True(replacement.IsSuccess);
        var replacementField = Assert.Single(replacement.Value.Draft.Fields);
        Assert.NotEqual(previousFieldId, replacementField.FieldId);
        Assert.Equal(catalogId, replacementField.Source!.Catalog);
        Assert.Equal(previousFieldId, Assert.Single(replacement.Value.IdentityMap.Fields).ReplacesFieldId);

        Assert.False(validator.Validate(request with
        {
            Draft = request.Draft with { Fields = [field with { Options = [new("manual", null, new Dictionary<string, string> { ["es"] = "Uno" })] }] },
        }).IsValid);
    }

    [Fact]
    public void Manual_draft_request_serialization_keeps_its_original_fingerprint_shape_and_catalog_source_changes_it()
    {
        var field = new FormFieldInput("question", null, null, "decimal",
            new Dictionary<string, string> { ["es"] = "Cantidad", ["en"] = "Amount" }, null, false, null, null);
        var manual = FormDraftPolicy.Serialize(field);
        Assert.Equal("{\"localId\":\"question\",\"fieldId\":null,\"replacesFieldId\":null,\"type\":\"decimal\",\"label\":{\"es\":\"Cantidad\",\"en\":\"Amount\"},\"help\":null,\"required\":false,\"maxLength\":null,\"options\":null}", manual);

        var catalogId = Guid.Parse("01990000-0000-7000-8000-000000000091");
        var catalog = FormDraftPolicy.Serialize(field with { Source = new FormQuestionSource(catalogId) });
        Assert.NotEqual(manual, catalog);
        Assert.Contains("\"source\":{\"catalog\":\"" + catalogId + "\"}", catalog, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(manual)),
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(catalog)));
    }
}
