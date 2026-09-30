using System.Reflection;
using ArquitecturaBase.ArchitectureTests.Support;
using StackExchange.Redis;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class CacheBoundaryTests
{
    private const string Adapter = "ArquitecturaBase.Infrastructure.Caching.RedisCache";
    private const string Registration = "ArquitecturaBase.Infrastructure.Caching.CachingRegistration";
    private static readonly Assembly[] Scanned =
        [Assembly.Load("ArquitecturaBase.Application"), Assembly.Load("ArquitecturaBase.Infrastructure"), Assembly.Load("ArquitecturaBase.Api")];

    [Fact]
    public void Only_the_cache_adapter_and_its_registration_use_the_Redis_client()
    {
        var owners = ClientOwners(Scanned.SelectMany(CallSites.TypeUses));
        Assert.Equal(new[] { Registration, Adapter }.Order(StringComparer.Ordinal), owners);
        Assert.Contains(typeof(DirectRedisWriter).FullName!, ClientOwners(CallSites.TypeUses(typeof(CacheBoundaryTests).Assembly)));
    }

    [Fact]
    public void Only_the_cache_adapter_writes_values_to_Redis()
    {
        var writers = Writers(Scanned.SelectMany(CallSites.Calls));
        Assert.Equal([Adapter], writers);
        Assert.Contains(typeof(DirectRedisWriter).FullName!, Writers(CallSites.Calls(typeof(CacheBoundaryTests).Assembly)));
    }

    [Fact]
    public void Application_data_caching_cannot_register_a_local_or_hybrid_provider()
    {
        var calls = Scanned.SelectMany(CallSites.Calls).ToArray();
        Assert.Contains(calls, call => call.Owner == Registration && call.Method == "AddRedisClient");
        Assert.DoesNotContain(calls, call => call.Method is "AddMemoryCache" or "AddDistributedMemoryCache" or "AddHybridCache");
        Assert.DoesNotContain(Scanned.SelectMany(assembly => assembly.GetReferencedAssemblies()),
            reference => reference.Name == "Microsoft.Extensions.Caching.Hybrid");
    }

    private static string[] ClientOwners(IEnumerable<CallSites.TypeUse> uses) =>
        [.. uses.Where(use => use.Type.StartsWith("StackExchange.Redis.", StringComparison.Ordinal))
            .Select(use => use.Owner).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static string[] Writers(IEnumerable<CallSites.Call> calls) =>
        [.. calls.Where(call => call.DeclaringType.StartsWith("StackExchange.Redis.", StringComparison.Ordinal)
                && call.Method is "StringSetAsync" or "ScriptEvaluateAsync")
            .Select(call => call.Owner).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}

// Control only: its IL must be detected; it is never executed.
file static class DirectRedisWriter
{
    public static Task<bool> Write(IConnectionMultiplexer connection) =>
        connection.GetDatabase().StringSetAsync("control", "value");
}
