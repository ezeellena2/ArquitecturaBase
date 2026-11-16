using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I4, the provider: a newer version saved a provider this one does not know (Gemini, here). Nothing answers 500: the connection reads
// empty and paused with its revision and a flag, the assistant is not available, the test never sends the saved key, the worker leaves
// a turn sealed with it for the version that knows it, and choosing a provider again saves over it.
[Collection(ApiTestGroup.Name)]
public sealed class AiConnectionUnsupportedProviderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_connection_saved_with_a_future_provider_reads_paused_and_unavailable_without_a_500()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "How many users are there?");
        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>().Database;
            await db.ExecuteSqlAsync($"""UPDATE "AiConnections" SET "Provider" = 'Gemini'""", Ct);
            return await db.ExecuteSqlAsync($"""UPDATE "AiAssistantTurns" SET "Provider" = 'Gemini' WHERE "RequestId" = {requestId}""", Ct);
        });

        using var connection = await scenario.Client.GetAsync("/api/ai/connection", Ct);
        using var bootstrap = await scenario.Client.GetAsync("/api/ai/assistant/bootstrap", Ct);
        var availability = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().GetAvailabilityAsync(Ct));
        using var probe = await scenario.Client.PostAsJsonAsync("/api/ai/connection/test", new
            { provider = "OpenAI", model = "fictional-model", credentialChange = "Keep", apiKey = (string?)null, expectedRevision = scenario.ConnectionRevision }, Ct);
        var claimed = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct));
        using var turn = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);

        Assert.Equal(HttpStatusCode.OK, connection.StatusCode);
        var shown = await connection.ReadJsonAsync();
        Assert.Equal(("OpenAI", "", false, false, scenario.ConnectionRevision, true), (shown.GetProperty("provider").GetString(), shown.GetProperty("model").GetString(),
            shown.GetProperty("hasCredential").GetBoolean(), shown.GetProperty("enabled").GetBoolean(), shown.GetProperty("revision").GetInt64(),
            shown.GetProperty("unsupportedProvider").GetBoolean()));
        Assert.Equal(HttpStatusCode.OK, bootstrap.StatusCode);
        Assert.Equal("paused", (await bootstrap.ReadJsonAsync()).GetProperty("availability").GetString());
        Assert.Equal("paused", availability.State);
        // The saved key is never sent to a provider this version knows: for it there is no key to keep (I4, review).
        Assert.Equal(HttpStatusCode.BadRequest, probe.StatusCode);
        Assert.Equal(AiConnectionErrors.CredentialRequired.Code, (await probe.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.True(claimed.IsSuccess);
        Assert.Null(claimed.Value);
        Assert.Equal(HttpStatusCode.OK, turn.StatusCode);
        Assert.Equal(AiAssistantTurnState.Pending, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .Set<AiAssistantTurn>().Where(row => row.RequestId == requestId).Select(row => row.State).SingleAsync(Ct)));

        // Choosing a provider again saves over the one this version does not know.
        var changeId = Guid.NewGuid();
        using var saved = await scenario.Client.PutAsJsonAsync("/api/ai/connection", new { provider = "OpenAI", model = "fictional-model",
            credentialChange = "Replace", apiKey = "fictional-private-key", enabled = true, expectedRevision = scenario.ConnectionRevision, requestId = changeId }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var replaced = (await saved.ReadJsonAsync()).GetProperty("connection");
        Assert.Equal(("OpenAI", true, false), (replaced.GetProperty("provider").GetString(), replaced.GetProperty("enabled").GetBoolean(),
            replaced.GetProperty("unsupportedProvider").GetBoolean()));
        Assert.Equal(AiProvider.OpenAI, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>()
            .Set<AiConnection>().Select(row => row.Provider).SingleAsync(Ct)));
    }

    // The screen shows it as OpenAI without a key and paused; whoever writes a model and saves sends Keep, which drops the saved key
    // instead of answering ProviderChanged for a change nobody made (I4, review).
    [Fact]
    public async Task Saving_without_touching_the_key_of_a_future_provider_saves_without_it()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Database
            .ExecuteSqlAsync($"""UPDATE "AiConnections" SET "Provider" = 'Gemini'""", Ct));

        using var saved = await scenario.Client.PutAsJsonAsync("/api/ai/connection", new { provider = "OpenAI", model = "fictional-model",
            credentialChange = "Keep", apiKey = (string?)null, enabled = false, expectedRevision = scenario.ConnectionRevision, requestId = Guid.NewGuid() }, Ct);

        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var shown = (await saved.ReadJsonAsync()).GetProperty("connection");
        Assert.Equal(("OpenAI", "fictional-model", false, false, false), (shown.GetProperty("provider").GetString(), shown.GetProperty("model").GetString(),
            shown.GetProperty("hasCredential").GetBoolean(), shown.GetProperty("enabled").GetBoolean(), shown.GetProperty("unsupportedProvider").GetBoolean()));
        var row = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<AiConnection>().AsNoTracking()
            .Select(connection => new { connection.Provider, connection.ProtectedApiKey }).SingleAsync(Ct));
        Assert.Equal((AiProvider.OpenAI, (string?)null), (row.Provider, row.ProtectedApiKey));
    }
}
