using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantConcurrencyTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_hosts_accept_an_identical_request_once_and_claim_it_once()
    {
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        await using var secondHost = factory.WithWebHostBuilder(_ => { });
        using var second = secondHost.CreateClient();
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, scenario.OwnerUserId.ToString());
        var requestId = Guid.NewGuid();
        var responses = await Task.WhenAll(scenario.Client.PostAsJsonAsync("/api/ai/assistant/turns", scenario.Payload(requestId), Ct),
            second.PostAsJsonAsync("/api/ai/assistant/turns", scenario.Payload(requestId), Ct));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.Equal((await firstResponse.ReadJsonAsync()).GetProperty("conversationId").GetGuid(), (await secondResponse.ReadJsonAsync()).GetProperty("conversationId").GetGuid());
        var claims = await Task.WhenAll(scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct)),
            ClaimOnSecondAsync());
        var claim = Assert.Single(claims.Select(result => result.Value), value => value is not null);
        Assert.Equal(requestId, claim!.RequestId);
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(1, await db.Set<AiAssistantRequestReceipt>().CountAsync(receipt => receipt.OwnerUserId == scenario.OwnerUserId && receipt.RequestId == requestId, Ct));
            Assert.Equal(1, await db.Set<AiAssistantTurn>().CountAsync(turn => turn.OwnerUserId == scenario.OwnerUserId && turn.RequestId == requestId, Ct));
        });
        await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().FailAsync(claim, AiAssistantErrors.ProviderRejected.Code, false, Ct));

        async Task<ArquitecturaBase.Domain.Results.Result<ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models.AiAssistantClaimResponse?>> ClaimOnSecondAsync()
        {
            await using var scope = secondHost.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct);
        }
    }

    [Fact]
    public async Task Acceptance_quota_is_shared_by_hosts_and_cannot_be_evaded_by_removing_content()
    {
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        await using var limitedHost = factory.WithWebHostBuilder(builder => builder.UseSetting("ArtificialIntelligence:Assistant:Limits:ActorDailyQuota", "1"));
        using var first = limitedHost.CreateClient();
        first.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, scenario.OwnerUserId.ToString());
        using var accepted = await first.PostAsJsonAsync("/api/ai/assistant/turns", scenario.Payload(Guid.NewGuid()), Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var conversationId = (await accepted.ReadJsonAsync()).GetProperty("conversationId").GetGuid();
        using var removed = await first.DeleteAsync($"/api/ai/assistant/conversations/{conversationId}", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        await using var secondHost = limitedHost.WithWebHostBuilder(_ => { });
        using var second = secondHost.CreateClient();
        second.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, scenario.OwnerUserId.ToString());
        using var exceeded = await second.PostAsJsonAsync("/api/ai/assistant/turns", scenario.Payload(Guid.NewGuid()), Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, exceeded.StatusCode);
        Assert.Equal("Ai.Assistant.QuotaExceeded", (await exceeded.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Independent_hosts_share_the_running_reservation_capacity()
    {
        using var firstActor = await AiAssistantTestScenario.CreateAsync(factory);
        using var secondActor = await AiAssistantTestScenario.CreateAsync(factory, configureConnection: false);
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        await firstActor.SubmitAsync(firstId);
        await secondActor.SubmitAsync(secondId);
        await using var firstHost = factory.WithWebHostBuilder(builder => builder.UseSetting("ArtificialIntelligence:Assistant:Limits:RunningCapacity", "1"));
        await using var secondHost = factory.WithWebHostBuilder(builder => builder.UseSetting("ArtificialIntelligence:Assistant:Limits:RunningCapacity", "1"));
        var results = await Task.WhenAll(ClaimAsync(firstHost.Services), ClaimAsync(secondHost.Services));
        var winner = Assert.Single(results.Select(result => result.Value), claim => claim is not null);
        Assert.All(results, result => Assert.True(result.IsSuccess));
        await using (var scope = firstHost.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().FailAsync(winner!, AiAssistantErrors.ProviderRejected.Code, false, Ct);
        using var canceledFirst = await firstActor.Client.PostAsync($"/api/ai/assistant/turns/{firstId}/cancel", null, Ct);
        using var canceledSecond = await secondActor.Client.PostAsync($"/api/ai/assistant/turns/{secondId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, canceledFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, canceledSecond.StatusCode);

        static async Task<ArquitecturaBase.Domain.Results.Result<AiAssistantClaimResponse?>> ClaimAsync(IServiceProvider services)
        {
            await using var scope = services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct);
        }
    }

    [Fact]
    public async Task Confirmed_cancellation_prevents_a_late_worker_from_publishing_its_candidate()
    {
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId);
        var claimed = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct));
        var claim = Assert.IsType<AiAssistantClaimResponse>(claimed.Value);
        var prepared = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantExecutionService>().PrepareAsync(claim, Ct));
        Assert.True(prepared.IsSuccess);
        Assert.True((await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().BeginProviderStepAsync(claim, Ct))).Value);
        using var canceled = await scenario.Client.PostAsync($"/api/ai/assistant/turns/{requestId}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, canceled.StatusCode);
        var candidate = new AiAssistantAdvanceResponse(prepared.Value, new("clarification", "Una precisión.", []), [], new(1, 1));
        var result = await scenario.ScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().CompleteAsync(claim, candidate, Ct));
        Assert.True(result.IsSuccess);
        using var recovered = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        var turn = await recovered.ReadJsonAsync();
        Assert.Equal("canceled", turn.GetProperty("state").GetString());
        Assert.False(turn.TryGetProperty("answer", out var answer) && answer.ValueKind == System.Text.Json.JsonValueKind.Object);
    }
}
