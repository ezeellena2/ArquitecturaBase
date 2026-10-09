using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantCapabilityTests
{
    [Fact]
    public void Installing_the_ai_module_contributes_the_assistant_capability_once()
    {
        var services = new ServiceCollection();
        services.AddArtificialIntelligenceApplication();
        services.AddArtificialIntelligenceApplication();
        using var provider = services.BuildServiceProvider();
        var sources = provider.GetServices<IClientCapabilitySource>().ToArray();
        var source = Assert.Single(sources);
        Assert.Equal(new[] { "assistant" }, source.InstalledFeatures);
    }
}
