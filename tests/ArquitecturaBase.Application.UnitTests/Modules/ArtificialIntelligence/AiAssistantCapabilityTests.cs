using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantCapabilityTests
{
    // I7: the place search is offered only where the installation has its key; without it the front does not show the search.
    [Theory]
    [InlineData(false, new[] { "assistant" })]
    [InlineData(true, new[] { "assistant", "place-search" })]
    public void Installing_the_ai_module_contributes_its_capabilities_once(bool placesConfigured, string[] expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBusinessPlacesClient>(new PlacesClient(placesConfigured));
        services.AddArtificialIntelligenceApplication();
        services.AddArtificialIntelligenceApplication();
        using var provider = services.BuildServiceProvider();
        var sources = provider.GetServices<IClientCapabilitySource>().ToArray();
        var source = Assert.Single(sources);
        Assert.Equal(expected, source.InstalledFeatures);
    }

    private sealed class PlacesClient(bool configured) : IBusinessPlacesClient
    {
        public bool IsConfigured => configured;
        public Task<Result<BusinessPlacesResponse>> SearchAsync(string input, string languageCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Publishing the capability never searches.");
    }
}
