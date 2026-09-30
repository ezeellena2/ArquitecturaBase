using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Caching;
using ArquitecturaBase.Infrastructure.Caching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal sealed class CacheTestHost(IHost host, CacheReadProbe probe, string prefix) : IAsyncDisposable
{
    public CacheReadProbe Probe { get; } = probe;
    public RedisCache Cache => host.Services.GetRequiredService<RedisCache>();
    public IConnectionMultiplexer Connection => host.Services.GetRequiredService<IConnectionMultiplexer>();
    public IServiceProvider Services => host.Services;
    public string Prefix { get; } = prefix;

    public static async Task<CacheTestHost> CreateAsync(string connectionString, string prefix,
        Func<CancellationToken, Task<string?>> read, CancellationToken cancellationToken,
        int waitSeconds = 30, int maximumPayloadBytes = 1_048_576)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing" });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:cache"] = connectionString,
            ["Caching:KeyPrefix"] = prefix,
            ["Caching:WaitSeconds"] = waitSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Caching:MaximumPayloadBytes"] = maximumPayloadBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        builder.AddRedisCaching();
        builder.Services.AddCaching(builder.Configuration, builder.Environment);
        var probe = new CacheReadProbe(read);
        builder.Services.AddSingleton(probe);
        builder.Services.AddScoped<ControlledCacheReader>();
        var host = builder.Build();
        try
        {
            await host.StartAsync(cancellationToken);
            return new CacheTestHost(host, probe, prefix);
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await host.StopAsync();
        host.Dispose();
    }
}
