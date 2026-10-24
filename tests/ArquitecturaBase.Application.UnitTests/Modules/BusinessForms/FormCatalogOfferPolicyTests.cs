using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogOfferPolicyTests
{
    [Theory]
    [InlineData("es", "true", "Activo")]
    [InlineData("en", "true", "Active")]
    [InlineData("es", "false", "Inactivo")]
    [InlineData("en", "false", "Inactive")]
    public void Boolean_source_values_use_the_recipient_culture(string culture, string value, string expected)
    {
        Assert.Equal(expected, FormCatalogOfferPolicy.LocalizeBoolean(value, culture));
    }

    [Fact]
    public void A_required_empty_catalog_blocks_sending()
    {
        Assert.NotNull(FormCatalogOfferPolicy.Validate(true, []));
        Assert.Null(FormCatalogOfferPolicy.Validate(false, []));
    }

    [Fact]
    public void More_than_two_hundred_options_are_rejected_without_truncating()
    {
        var options = Enumerable.Range(0, 201).Select(_ => new StructuredFormCatalogOption(Guid.NewGuid(), "Option", null)).ToArray();
        Assert.NotNull(FormCatalogOfferPolicy.Validate(false, options));
        Assert.Null(FormCatalogOfferPolicy.Validate(true, options[..200]));
        Assert.Equal(201, options.Length);
    }

    [Fact]
    public void An_option_with_only_its_English_original_text_is_valid_for_a_Spanish_original_form()
    {
        // The option comes from a catalog written in English: its original is English whatever the form's language is.
        var option = new StructuredFormCatalogOption(Guid.NewGuid(), "North branch", null,
            new Dictionary<string, StructuredFormCatalogOptionText> { ["en"] = new("North branch", null) });

        Assert.Null(FormCatalogOfferPolicy.Validate(true, [option]));
    }

    [Fact]
    public void Unrepresentable_options_are_rejected_without_cutting_their_text()
    {
        Assert.NotNull(FormCatalogOfferPolicy.Validate(true, [new(Guid.NewGuid(), new string('x', 31), null)]));
        Assert.NotNull(FormCatalogOfferPolicy.Validate(true, [new(Guid.NewGuid(), "Option", new string('x', 301))]));
    }
}
