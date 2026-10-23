using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogShownOfferTests
{
    [Theory]
    [InlineData(30, 300, true)]
    [InlineData(31, 300, false)]
    [InlineData(30, 301, false)]
    [InlineData(0, 0, false)]
    public void The_sealed_shown_text_is_checked_at_the_offer_boundary(int titleLength, int descriptionLength, bool valid)
    {
        var option = new StructuredFormCatalogOption(Guid.CreateVersion7(), "Original", "Original detail",
            Shown: new(new string('t', titleLength), descriptionLength == 0 ? null : new string('d', descriptionLength)));
        Assert.Equal(!valid, FormCatalogOfferPolicy.IsInvalid(option));
        Assert.Equal(valid ? null : FormRecordErrors.CatalogOptionInvalid.Code,
            FormCatalogOfferPolicy.Validate(true, [option]));
    }

    [Fact]
    public void Old_offers_without_a_shown_presentation_keep_the_previous_boundary()
    {
        Assert.False(FormCatalogOfferPolicy.IsInvalid(new(Guid.CreateVersion7(), "Original", "Detail")));
        Assert.True(FormCatalogOfferPolicy.IsInvalid(new(Guid.CreateVersion7(), new string('t', 31), "Detail")));
    }
}
