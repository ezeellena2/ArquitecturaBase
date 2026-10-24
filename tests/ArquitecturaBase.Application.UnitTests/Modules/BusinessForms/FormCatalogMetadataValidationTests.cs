using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using FluentValidation.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogMetadataValidationTests
{
    private static readonly FormCatalogFilterDefinition Filters = new("name", null, [], false, "name", false);

    [Theory]
    [InlineData("omitted")]
    [InlineData("empty")]
    [InlineData("blank")]
    public void Catalog_description_is_optional_for_manual_and_filters_catalogs(string shape)
    {
        var description = Description(shape);

        foreach (var result in Validate(description))
            Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("fr", 10)]
    [InlineData("es", 1001)]
    public void An_optional_description_still_keeps_its_languages_and_length(string culture, int length)
    {
        var description = new Dictionary<string, string?> { [culture] = new string('a', length) };

        foreach (var result in Validate(description))
            Assert.Contains(result.Errors, error => error.PropertyName == "description." + culture);
    }

    private static IEnumerable<ValidationResult> Validate(IReadOnlyDictionary<string, string?>? description) =>
    [
        new CreateManualCatalogRequestValidator().Validate(new CreateManualCatalogRequest(Name(), description, [])),
        new UpdateManualCatalogRequestValidator().Validate(new UpdateManualCatalogRequest(1, Name(), description, [])),
        new CreateFiltersCatalogRequestValidator().Validate(new CreateFiltersCatalogRequest(Name(), description, Filters)),
        new UpdateFiltersCatalogRequestValidator().Validate(new UpdateFiltersCatalogRequest(1, Name(), description, Filters)),
    ];

    private static Dictionary<string, string?> Name() => new() { ["es"] = "Zonas", ["en"] = "Areas" };

    private static Dictionary<string, string?>? Description(string shape) => shape switch
    {
        "omitted" => null,
        "empty" => [],
        _ => new() { ["es"] = "  ", ["en"] = null },
    };
}
