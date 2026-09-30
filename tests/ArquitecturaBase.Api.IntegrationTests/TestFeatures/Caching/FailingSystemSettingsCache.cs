using ArquitecturaBase.Application.Interfaces.Integrations.Caching;
using StackExchange.Redis;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Caching;

internal sealed class FailingSystemSettingsCache : ISystemSettingsCache
{
    public Task InvalidateAsync(CancellationToken cancellationToken) =>
        throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "The test Redis connection is unavailable.");
}
