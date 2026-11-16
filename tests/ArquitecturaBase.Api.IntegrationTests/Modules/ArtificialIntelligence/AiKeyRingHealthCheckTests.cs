using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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

    // I17, review: the readiness only takes a copy out of HTTP traffic. Its worker asks the same verdict before taking a turn, and its
    // channels' availability too: a turn that the copies with the right ring accepted stays pending for them. One copy has another
    // InstallationId (it could not read the saved key and would close the turn Unavailable); the other reads it, but its active key is
    // one the others do not have (it would answer encrypting with it).
    [Fact]
    public async Task A_copy_with_another_ring_takes_no_turn_and_offers_nothing_to_the_channels()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "How many users are there?");
        await using var otherInstallation = scenario.CreateAdditionalHost(services => services.AddSingleton(Ring("another-installation", KeyId)));
        await using var otherActiveKey = scenario.CreateAdditionalHost(services => services.AddSingleton(Ring("integration-installation", OtherKeyId)));

        foreach (var copy in new[] { otherInstallation, otherActiveKey })
        {
            Assert.False(await copy.Services.GetRequiredService<AiAssistantCoordinator>().ProcessNextAsync(Ct));
            await using var scope = copy.Services.CreateAsyncScope();
            Assert.Equal("notConfigured", (await scope.ServiceProvider.GetRequiredService<IAssistantOperations>().GetAvailabilityAsync(Ct)).State);
        }
        using var turn = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal("pending", (await turn.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.Empty(scenario.Handler.Requests);
    }

    // I17, review: two copies that start at once on an empty table. The lock puts them in line and the second compares with the control
    // the first wrote: neither breaks the unique index and both are ready. The ring is the factory's, so the rows it writes back are the
    // same ones the other tests read.
    [Fact]
    public async Task Two_copies_that_start_at_once_on_an_empty_table_are_both_ready()
    {
        await factory.ExecuteDbContextAsync(dbContext => dbContext.Set<AiKeyRingControl>().ExecuteDeleteAsync(Ct));
        await using var first = factory.WithWebHostBuilder(_ => { });
        await using var second = factory.WithWebHostBuilder(_ => { });
        var checks = new[] { first, second }.Select(copy => copy.Services.GetRequiredService<HealthCheckService>()).ToArray();

        var reports = await Task.WhenAll(checks.Select(check => check.CheckHealthAsync(entry => entry.Name == AiKeyRingHealthCheck.Name, Ct)));

        Assert.All(reports, report => Assert.Equal(HealthStatus.Healthy, report.Entries[AiKeyRingHealthCheck.Name].Status));
        Assert.Equal([KeyId], await factory.ExecuteDbContextAsync(dbContext => dbContext.Set<AiKeyRingControl>().Select(control => control.KeyId).ToListAsync(Ct)));
    }

    private static AiKeyRing Ring(string installationId, string activeKeyId) => new(Options.Create(new AiSecretProtectionOptions
    {
        InstallationId = installationId, ActiveKeyId = activeKeyId,
        Keys = { [KeyId] = ApiFactory.TestHashKey, [OtherKeyId] = Convert.ToBase64String(Enumerable.Repeat((byte)9, 32).ToArray()) },
    }));

    private async Task AssertNotReadyAsync(string setting, string value, string written)
    {
        await using var copy = factory.WithWebHostBuilder(builder => builder.UseSetting("ArtificialIntelligence:SecretProtection:" + setting, value));
        using var client = copy.CreateClient();
        using var ready = await client.GetAsync("/health", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        var entry = await CheckAsync(copy);
        Assert.Equal(HealthStatus.Unhealthy, entry.Status);
        Assert.Contains("'" + KeyId + "'", entry.Description, StringComparison.Ordinal);
        // I17, review: the first copy that wrote the control may be the wrong one: the description sends to the guide that says which
        // ring is right, not to align with the other copies.
        Assert.Contains("docs/guides/configurar-ia.md", entry.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("as the other copies", entry.Description, StringComparison.Ordinal);
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
