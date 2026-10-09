using ArquitecturaBase.Application.Models.Settings;

namespace ArquitecturaBase.Application.UnitTests.Channels;

public sealed class ClientCapabilityTests
{
    [Fact]
    public void Presentation_has_an_empty_installed_feature_collection_by_default()
    {
        Assert.Empty(SystemPresentationResponse.Defaults.InstalledFeatures);
    }
}
