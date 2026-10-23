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
    public void Only_the_original_attribute_name_is_required_and_source_configuration_must_match_the_type()
    {
        var validator = new CreateFormPersonAttributeRequestValidator();
        var request = new CreateFormPersonAttributeRequest("zone", new Dictionary<string, string?> { ["es"] = "Zona", ["en"] = "" }, "Catalog", Guid.CreateVersion7());
        Assert.True(validator.Validate(request).IsValid);
        Assert.Contains(validator.Validate(request with { SourceCatalogId = null }).Errors, error => error.PropertyName == "sourceCatalogId");
        Assert.Contains(validator.Validate(request with { Type = "Text" }).Errors, error => error.PropertyName == "sourceCatalogId");
        Assert.Contains(validator.Validate(request with { Name = new Dictionary<string, string?> { ["en"] = "Zone" } }).Errors,
            error => error.PropertyName == "name.es");
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
