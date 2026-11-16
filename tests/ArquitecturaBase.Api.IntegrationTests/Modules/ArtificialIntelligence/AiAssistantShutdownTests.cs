using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I12 with PostgreSQL and the real engine: what a deployment does to a turn in progress.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantShutdownTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_turn_the_stopping_copy_gives_back_before_any_step_is_taken_and_completed_by_another_copy()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "¿Cuántos usuarios activos hay?");
        using var stop = new CancellationTokenSource();
        await using var stopping = scenario.CreateAdditionalHost(services => StoppingExecutionService.Decorate(services, stop.Cancel));

        Assert.True(await stopping.Services.GetRequiredService<AiAssistantCoordinator>().ProcessNextAsync(stop.Token, Ct));

        var given = await TurnAsync(scenario, requestId);
        Assert.Equal(("pending", JsonValueKind.Null), (given.GetProperty("state").GetString(), given.GetProperty("failure").ValueKind));
        Assert.Empty(scenario.Handler.Requests);
        Assert.False(await stopping.Services.GetRequiredService<AiAssistantCoordinator>().ProcessNextAsync(stop.Token, Ct));

        Assert.True(await scenario.ProcessNextAsync());

        var completed = await TurnAsync(scenario, requestId);
        Assert.Equal(("completed", JsonValueKind.Null), (completed.GetProperty("state").GetString(), completed.GetProperty("failure").ValueKind));
        var turn = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<AiAssistantTurn>()
            .AsNoTracking().SingleAsync(row => row.RequestId == requestId, Ct));
        // Claimed twice: the copy that stopped and the one that completed it, each with its own fence.
        Assert.Equal((AiAssistantTurnState.Completed, null, 2L), (turn.State, turn.FailureCode, turn.Fence));
    }

    [Fact]
    public async Task A_step_the_end_of_the_drain_cuts_stays_uncertain_with_the_shutdown_code_and_its_text()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "¿Cuántos usuarios activos hay?");
        using var stop = new CancellationTokenSource();
        using var drain = new CancellationTokenSource();
        scenario.Handler.BeforeAnswer = async _ => { await stop.CancelAsync(); await drain.CancelAsync(); };

        Assert.True(await scenario.ScopeAsync(services => services.GetRequiredService<AiAssistantCoordinator>().ProcessNextAsync(stop.Token, drain.Token)));

        var turn = await TurnAsync(scenario, requestId);
        var failure = turn.GetProperty("failure");
        Assert.Equal(("uncertain", AiAssistantErrors.Interrupted.Code, "reviewUncertainty"),
            (turn.GetProperty("state").GetString(), failure.GetProperty("code").GetString(), failure.GetProperty("recovery").GetString()));
        Assert.Matches("se reinició|restarted", failure.GetProperty("detail").GetString()!);
        Assert.Single(scenario.Handler.Requests);
        Assert.False(await scenario.ProcessNextAsync());
    }

    private static async Task<JsonElement> TurnAsync(AiAssistantExecutionScenario scenario, Guid requestId)
    {
        using var response = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }
}
