using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantExecutionScenario(WebApplicationFactory<Program> host, HttpClient client,
    long connectionRevision, long businessRevision, Guid supportRoleId,
    AiAssistantExecutionProviderHandler handler, AiAssistantTransactionProbe transactions) : IAsyncDisposable
{
    public HttpClient Client { get; } = client;
    public long ConnectionRevision { get; } = connectionRevision;
    public long BusinessRevision { get; } = businessRevision;
    public Guid SupportRoleId { get; } = supportRoleId;
    public AiAssistantExecutionProviderHandler Handler { get; } = handler;
    public AiAssistantTransactionProbe Transactions { get; } = transactions;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<AiAssistantExecutionScenario> CreateAsync(ApiFactory factory, AiProvider provider, string toolId)
    {
        var transactions = new AiAssistantTransactionProbe();
        var handler = new AiAssistantExecutionProviderHandler(provider, toolId, transactions);
        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("assistant_execution"));
            builder.UseSetting("ArtificialIntelligence:Assistant:ProcessInBackground", "false");
            builder.UseSetting("Logging:LogLevel:Default", "Warning");
            builder.ConfigureTestServices(services =>
            {
                services.AddDbContext<ApplicationDbContext>((_, options) => options.AddInterceptors(transactions));
                services.AddHttpClient(provider == AiProvider.OpenAI
                    ? AiAssistantProvidersRegistration.OpenAiConversationHttpClientName
                    : AiAssistantProvidersRegistration.AnthropicConversationHttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });
        var client = host.CreateClient();
        await using (var scope = host.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(Ct);
        await host.Services.SeedDatabaseAsync();
        Guid ownerId = default;
        Guid roleId = default;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var services = scope.ServiceProvider;
            var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async ct =>
            {
                var roles = services.GetRequiredService<IRoleRepository>();
                roleId = await roles.CreateAsync("Support", "Confirmed support role", [Permissions.Users.Read], ct);
                await roles.CreateAsync("Assistant operator", "Integration operator", Permissions.All.Concat(ArtificialIntelligencePermissions.All).ToArray(), ct);
                var users = services.GetRequiredService<IUserRepository>();
                var owner = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-execution")).Value, null, false, null, "es", ct, "America/New_York");
                ownerId = owner.Id;
                await users.SetRolesAsync(ownerId, ["Assistant operator"], ct);
                await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-counted")).Value, null, false, null, "en", ct);
                var inactive = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-inactive")).Value, null, false, null, "es", ct);
                await users.SetActiveAsync(inactive.Id, false, ct);
                await users.SetRolesAsync(inactive.Id, ["Support"], ct);
                var deleted = await users.CreateAsync(Email.Create(TestEmails.Unique("assistant-deleted")).Value, null, false, null, "es", ct);
                await users.SetRolesAsync(deleted.Id, ["Support"], ct);
                await users.DeleteAsync(deleted.Id, ct);
                return Result.Success();
            }, CommitPolicy.OnSuccess, Ct);
            Assert.True(result.IsSuccess);
        }
        handler.RoleId = roleId;
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, ownerId.ToString());
        using var current = await client.GetAsync("/api/ai/connection", Ct);
        var revision = (await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
        using var configured = await client.PutAsJsonAsync("/api/ai/connection", new
        {
            requestId = Guid.NewGuid(), provider = provider.ToString(), model = "fictional-model", credentialChange = "Replace",
            apiKey = "fictional-assistant-key", enabled = true, expectedRevision = revision,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, configured.StatusCode);
        revision = (await configured.ReadJsonAsync()).GetProperty("connection").GetProperty("revision").GetInt64();
        using var currentBusiness = await client.GetAsync("/api/ai/business-information", Ct);
        var businessRevision = (await currentBusiness.ReadJsonAsync()).GetProperty("revision").GetInt64();
        using var business = await client.PutAsJsonAsync("/api/ai/business-information", new
        {
            requestId = Guid.NewGuid(), expectedRevision = businessRevision, name = "Integration clinic", description = "Confirmed business description",
            hours = "09:00–17:00", location = "Example street 12", services = "Confirmed consultation", policies = "Appointment required", faq = "Bring your document",
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, business.StatusCode);
        businessRevision = (await business.ReadJsonAsync()).GetProperty("revision").GetInt64();
        return new(host, client, revision, businessRevision, roleId, handler, transactions);
    }

    public async Task<JsonElement> SubmitAsync(Guid requestId, string question, Guid? conversationId = null, long? expectedRevision = null)
    {
        using var response = await Client.PostAsJsonAsync("/api/ai/assistant/turns", new
        {
            requestId, question, conversationId, expectedRevision, expectedConnectionRevision = ConnectionRevision,
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    public Task<bool> ProcessNextAsync() => host.Services.GetRequiredService<AiAssistantCoordinator>().ProcessNextAsync(Ct);

    public async Task<T> ScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.DisposeAsync();
    }
}
