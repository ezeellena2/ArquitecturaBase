using System.Reflection;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Caching;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Caching;

[Collection(ApiTestGroup.Name)]
public sealed class RedisCacheTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly RedisCacheKey Key = new("tests:value");

    [Fact]
    public void Redis_is_registered_as_the_only_application_data_cache()
    {
        var infrastructure = Assembly.Load("ArquitecturaBase.Infrastructure");
        var redisCache = infrastructure.GetType("ArquitecturaBase.Infrastructure.Caching.RedisCache");

        Assert.NotNull(redisCache);
        Assert.NotNull(factory.Services.GetService(redisCache));
        Assert.DoesNotContain(infrastructure.GetReferencedAssemblies(),
            reference => reference.Name == "Microsoft.Extensions.Caching.Hybrid");
    }

    [Fact]
    public async Task Two_independent_hosts_share_values_and_invalidation()
    {
        var prefix = NewPrefix();
        var current = "first";
        await using var first = await HostAsync(prefix, _ => Task.FromResult<string?>(current));
        await using var second = await HostAsync(prefix, _ => Task.FromResult<string?>(current));
        Assert.NotSame(first.Connection, second.Connection);

        Assert.Equal("first", await ReadAsync(first));
        current = "second";
        Assert.Equal("first", await ReadAsync(second));
        Assert.Equal(0, second.Probe.Calls);

        await second.Cache.RemoveAsync(Key, Ct);
        Assert.Equal("second", await ReadAsync(first));
        Assert.Equal("second", await ReadAsync(second));
        Assert.Equal(2, first.Probe.Calls);
        Assert.Equal(0, second.Probe.Calls);
    }

    [Fact]
    public async Task Null_values_are_cached_and_prefixes_are_isolated()
    {
        await using var first = await HostAsync(NewPrefix(), _ => Task.FromResult<string?>(null));
        await using var second = await HostAsync(NewPrefix(), _ => Task.FromResult<string?>("other"));
        Assert.Null(await ReadAsync(first));
        Assert.Null(await ReadAsync(first));
        Assert.Equal(1, first.Probe.Calls);
        Assert.Equal("other", await ReadAsync(second));
    }

    [Fact]
    public async Task The_factory_uses_a_disposed_scope_distinct_from_the_callers_scope()
    {
        await using var host = await HostAsync(NewPrefix(), _ => Task.FromResult<string?>("value"));
        await using var caller = host.Services.CreateAsyncScope();
        var callerReader = caller.ServiceProvider.GetRequiredService<ControlledCacheReader>();

        Assert.Equal("value", await ReadAsync(host));

        Assert.False(callerReader.Disposed);
        var factoryReader = Assert.Single(host.Probe.Readers, reader => reader != callerReader);
        Assert.True(factoryReader.Disposed);
    }

    [Fact]
    public async Task Values_expire_in_Redis_and_are_read_again()
    {
        await using var host = await HostAsync(NewPrefix(), _ => Task.FromResult<string?>("value"));
        await ReadAsync(host);
        var database = host.Connection.GetDatabase();
        var ttl = await database.KeyTimeToLiveAsync(Key.DataKey(host.Prefix));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value.TotalSeconds, 1, 60);

        await database.KeyExpireAsync(Key.DataKey(host.Prefix), TimeSpan.Zero);
        await ReadAsync(host);
        Assert.Equal(2, host.Probe.Calls);
    }

    [Fact]
    public async Task Oversized_results_are_returned_without_being_stored()
    {
        var large = new string('x', 200);
        await using var host = await CacheTestHost.CreateAsync(factory.RedisConnectionString, NewPrefix(),
            _ => Task.FromResult<string?>(large), Ct, maximumPayloadBytes: 100);
        Assert.Equal(large, await ReadAsync(host));
        Assert.Equal(large, await ReadAsync(host));
        Assert.Equal(2, host.Probe.Calls);
        Assert.False(await host.Connection.GetDatabase().KeyExistsAsync(Key.DataKey(host.Prefix)));
    }

    internal Task<CacheTestHost> HostAsync(string prefix, Func<CancellationToken, Task<string?>> read) =>
        CacheTestHost.CreateAsync(factory.RedisConnectionString, prefix, read, Ct);

    internal static string NewPrefix() => "cache-tests:" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);

    internal static Task<string?> ReadAsync(CacheTestHost host, CancellationToken? cancellationToken = null) =>
        host.Cache.GetOrCreateInOwnScopeAsync<ControlledCacheReader, int, string?>(Key, 0,
            static (reader, _, ct) => reader.ReadAsync(ct), TimeSpan.FromSeconds(60), cancellationToken ?? Ct);
}
