using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

// I17, review: a key ring already verified against the control values, for the coordinator tests whose services are doubles.
internal sealed class MatchingKeyRing : IAiKeyRingReadiness
{
    public static MatchingKeyRing Instance { get; } = new();
    public Task<bool> MatchesAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
