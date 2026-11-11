using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFeedLimitsTests
{
    [Fact]
    public void A_feed_promises_the_text_an_option_of_forms_admits()
    {
        Assert.Equal(BusinessFormCatalogItem.MaxTextLength, StructuredFeedItem.MaxTextLength);
    }
}
