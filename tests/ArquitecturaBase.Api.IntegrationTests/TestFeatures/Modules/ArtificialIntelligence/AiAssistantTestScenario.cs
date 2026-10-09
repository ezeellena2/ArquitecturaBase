using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantTestScenario(ApiFactory factory, HttpClient client, Guid ownerUserId, long connectionRevision) : IDisposable
{
    public HttpClient Client { get; } = client;
    public Guid OwnerUserId { get; } = ownerUserId;
    public long ConnectionRevision { get; } = connectionRevision;
    public static async Task<AiAssistantTestScenario> CreateAsync(ApiFactory factory, bool configureConnection = true)
    {
        var ct = TestContext.Current.CancellationToken;
        var owner = await factory.InTransactionAsync(async services =>
        {
            var name = "assistant-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null,
                Permissions.All.Concat(ArtificialIntelligencePermissions.All).ToArray(), ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-owner")).Value, null, false, null, "es", ct);
            await users.SetRolesAsync(user.Id, [name], ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, owner.ToString());
        using var current = await client.GetAsync("/api/ai/connection", ct);
        var revision = (await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
        if (!configureConnection) return new(factory, client, owner, revision);
        using var saved = await client.PutAsJsonAsync("/api/ai/connection", new
        {
            requestId = Guid.NewGuid(), provider = "OpenAI", model = "fictional-model", credentialChange = "Replace",
            apiKey = "fictional-assistant-key", enabled = true, expectedRevision = revision,
        }, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        return new(factory, client, owner, (await saved.ReadJsonAsync()).GetProperty("connection").GetProperty("revision").GetInt64());
    }

    public async Task<JsonElement> SubmitAsync(Guid requestId, string question = "¿Cuántos usuarios activos hay?")
    {
        using var response = await Client.PostAsJsonAsync("/api/ai/assistant/turns", Payload(requestId, question), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    public object Payload(Guid requestId, string question = "¿Cuántos usuarios activos hay?") => new
    {
        requestId, conversationId = (Guid?)null, expectedRevision = (long?)null, expectedConnectionRevision = ConnectionRevision, question,
    };

    public Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> work) => factory.ExecuteScopeAsync(work);
    public void Dispose() => Client.Dispose();
}
