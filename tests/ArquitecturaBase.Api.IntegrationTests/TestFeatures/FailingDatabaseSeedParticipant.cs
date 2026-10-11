using ArquitecturaBase.Infrastructure.Persistence.Seed;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

internal sealed class FailingDatabaseSeedParticipant : IDatabaseSeedParticipant
{
    public Task SeedAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Synthetic optional seed failure.");
}
