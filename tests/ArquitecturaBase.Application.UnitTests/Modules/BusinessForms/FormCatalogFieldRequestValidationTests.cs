using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldRequestValidationTests
{
    [Fact]
    public void Item_field_keys_are_saved_ids_and_values_are_bounded()
    {
        var fieldId = Guid.CreateVersion7().ToString("D");
        var item = new FormCatalogManualItemSaveRequest("one", null, "one", Texts(), null, true, Fields: new Dictionary<string, string?> { ["temporary"] = "value", [fieldId] = new string('x', 301) });
        var result = new CreateManualCatalogRequestValidator().Validate(new CreateManualCatalogRequest(Texts(), Texts(), [item]));
        Assert.Contains(result.Errors, error => error.PropertyName == "items[0].fields.temporary");
        Assert.Contains(result.Errors, error => error.PropertyName == "items[0].fields." + fieldId);
    }

    [Fact]
    public void Presentation_lists_cannot_be_null_empty_or_exceed_existing_field_capacity()
    {
        var request = new CreateManualCatalogRequest(Texts(), Texts(), [], Presentation: new([], null!));
        var result = new CreateManualCatalogRequestValidator().Validate(request);
        Assert.Contains(result.Errors, error => error.PropertyName == "presentation.title");
        Assert.Contains(result.Errors, error => error.PropertyName == "presentation.description");
    }

    [Fact]
    public void Fields_must_have_identity_names_types_and_a_valid_position()
    {
        var field = new FormCatalogFieldSaveRequest(null, null, "field", null, null, null, false, false, null, null, null, null, -1, true);
        var result = new CreateManualCatalogRequestValidator().Validate(new CreateManualCatalogRequest(Texts(), Texts(), [], Fields: [field]));
        Assert.Contains(result.Errors, error => error.PropertyName == "fields[0].localId");
        Assert.Contains(result.Errors, error => error.PropertyName == "fields[0].name");
        Assert.Contains(result.Errors, error => error.PropertyName == "fields[0].type");
        Assert.Contains(result.Errors, error => error.PropertyName == "fields[0].position");
    }

    [Fact]
    public void A_field_name_in_a_single_language_has_a_valid_shape()
    {
        var english = new FormCatalogFieldSaveRequest("region", null, "region", new Dictionary<string, string?> { ["en"] = "Region", ["es"] = null },
            "text", null, false, false, null, null, 300, "data", 0, true);
        var unknown = english with { Name = new Dictionary<string, string?> { ["fr"] = "Région" } };
        var validator = new CreateManualCatalogRequestValidator();

        Assert.True(validator.Validate(new CreateManualCatalogRequest(Texts(), null, [], Fields: [english])).IsValid);
        Assert.Contains(validator.Validate(new CreateManualCatalogRequest(Texts(), null, [], Fields: [unknown])).Errors,
            error => error.PropertyName == "fields[0].name");
    }

    private static Dictionary<string, string?> Texts() => new Dictionary<string, string?> { ["es"] = "Catálogo", ["en"] = "Catalog" };
}
