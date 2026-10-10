using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormPublicationCompositionTests
{
    [Fact]
    public void Duplicate_channel_identifiers_fail_configuration_instead_of_selecting_the_first()
    {
        var channel = new StubStructuredFormChannel("same");
        Assert.Throws<InvalidOperationException>(() => new BusinessFormPublicationService(null!, null!, null!, null!, [channel, channel], null!, null!, null!));
    }
    [Fact]
    public void Empty_channel_identifiers_fail_configuration()
    {
        Assert.Throws<InvalidOperationException>(() => new BusinessFormPublicationService(null!, null!, null!, null!, [new StubStructuredFormChannel("")], null!, null!, null!));
    }
}