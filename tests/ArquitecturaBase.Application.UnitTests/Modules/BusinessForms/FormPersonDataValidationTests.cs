using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormPersonDataValidationTests
{
    [Theory]
    [InlineData("zone", true)]
    [InlineData("zone_2", true)]
    [InlineData("Zone", false)]
    [InlineData("2zone", false)]
    [InlineData("a-b", false)]
    public void Attribute_keys_use_a_stable_lowercase_identifier(string key, bool valid)
    {
        var request = new CreateFormPersonAttributeRequest(key, new Dictionary<string, string?> { ["es"] = "Zona" }, "Text", null);
        Assert.Equal(valid, new CreateFormPersonAttributeRequestValidator().Validate(request).IsValid);
    }

    [Fact]
    public void Attribute_names_need_only_a_valid_shape_and_source_configuration_must_match_the_type()
    {
        var validator = new CreateFormPersonAttributeRequestValidator();
        var request = new CreateFormPersonAttributeRequest("zone", new Dictionary<string, string?> { ["es"] = "Zona", ["en"] = "" }, "Catalog", Guid.CreateVersion7());
        Assert.True(validator.Validate(request).IsValid);
        Assert.Contains(validator.Validate(request with { SourceCatalogId = null }).Errors, error => error.PropertyName == "sourceCatalogId");
        Assert.Contains(validator.Validate(request with { Type = "Text" }).Errors, error => error.PropertyName == "sourceCatalogId");
        // The required language is the original one, which the service knows; the validator accepts a name in either language.
        Assert.True(validator.Validate(request with { Name = new Dictionary<string, string?> { ["en"] = "Zone" } }).IsValid);
        Assert.Contains(validator.Validate(request with { Name = null }).Errors, error => error.PropertyName == "name");
        Assert.Contains(validator.Validate(request with { Name = new Dictionary<string, string?> { ["fr"] = "Zone" } }).Errors,
            error => error.PropertyName == "name.fr");
    }

    [Fact]
    public void Person_search_requests_do_not_expose_private_search_terms_when_formatted()
    {
        const string search = "Nombre privado";
        Assert.DoesNotContain(search, new ListFormPersonDataRequest { Search = search }.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(search, new ListFormPersonAttributeOptionsRequest { Search = search }.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Person_value_requests_have_a_bounded_revision_and_canonical_attribute_ids()
    {
        var validator = new UpdateFormPersonDataRequestValidator();
        var request = new UpdateFormPersonDataRequest(null, new Dictionary<string, string?> { [Guid.CreateVersion7().ToString("D")] = null });
        Assert.True(validator.Validate(request).IsValid);
        Assert.False(validator.Validate(request with { ExpectedRevision = 0 }).IsValid);
        Assert.False(validator.Validate(request with { ExpectedRevision = 9007199254740992 }).IsValid);
        Assert.False(validator.Validate(request with { Values = new Dictionary<string, string?> { ["not-an-id"] = "private" } }).IsValid);
        Assert.False(validator.Validate(request with { Values = null }).IsValid);
    }
}
