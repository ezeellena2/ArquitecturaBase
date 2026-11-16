using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I17: a copy whose key ring differs from the one that wrote the control values is not ready, without decrypting anything.
[Collection(ApiTestGroup.Name)]
public sealed class AiKeyRingHealthCheckTests(ApiFactory factory)
{
    private const string KeyId = "test";
    private const string OtherKeyId = "other-ring";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_copy_with_another_ring_for_the_same_key_is_not_ready_and_one_with_the_same_ring_is()
    {
        // The factory's copy checks first: on a base without controls it writes the one of its key.
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(factory)).Status);
        var written = await ControlAsync();
        Assert.NotNull(written);

        await AssertNotReadyAsync("InstallationId", "another-installation", written);
        await AssertNotReadyAsync("Keys:" + KeyId, Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray()), written);
        // A copy that does not match writes nothing: the control is still the first one.
        Assert.Equal(written, await ControlAsync());

        await using var sameRing = factory.WithWebHostBuilder(_ => { });
        using var client = sameRing.CreateClient();
        using var ready = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    // I17, review: a ring of other keys has nothing to compare, but its active key has no control while others do: it is not ready and
    // writes nothing, or it would encrypt with a key the other copies do not have.
    [Fact]
    public async Task A_copy_whose_active_key_has_no_control_while_others_do_is_not_ready_and_writes_nothing()
    {
        Assert.Equal(HealthStatus.Healthy, (await CheckAsync(factory)).Status);
        await using var copy = factory.WithWebHostBuilder(builder => builder
            .UseSetting("ArtificialIntelligence:SecretProtection:ActiveKeyId", OtherKeyId)
            .UseSetting("ArtificialIntelligence:SecretProtection:Keys:" + OtherKeyId, Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray()))
            .ConfigureTestServices(services => services.PostConfigure<AiSecretProtectionOptions>(options => options.Keys.Remove(KeyId))));
        using var client = copy.CreateClient();
        using var ready = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        Assert.Contains("'" + OtherKeyId + "'", (await CheckAsync(copy)).Description, StringComparison.Ordinal);
        Assert.False(await factory.ExecuteDbContextAsync(dbContext => dbContext.Set<AiKeyRingControl>().AnyAsync(control => control.KeyId == OtherKeyId, Ct)));
    }

    private async Task AssertNotReadyAsync(string setting, string value, string written)
    {
        await using var copy = factory.WithWebHostBuilder(builder => builder.UseSetting("ArtificialIntelligence:SecretProtection:" + setting, value));
        using var client = copy.CreateClient();
        using var ready = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var entry = await CheckAsync(copy);
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("'" + KeyId + "'", entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(value, entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain(written, entry.Description, StringComparison.Ordinal);
        // Restarting does not fix a wrong ring: liveness stays up.
        using var live = await client.GetAsync("/alive", Ct);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    private static async Task<HealthReportEntry> CheckAsync(WebApplicationFactory<Program> host)
    {
        var report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);
        return report.Entries[AiKeyRingHealthCheck.Name];
    }

    private Task<string?> ControlAsync() => factory.ExecuteDbContextAsync(dbContext => dbContext.Set<AiKeyRingControl>()
        .Where(control => control.KeyId == KeyId).Select(control => control.Control).SingleOrDefaultAsync(Ct));
}
