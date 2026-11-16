using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I10: a turn keeps the trace context of whoever submitted it (the request in Home, the round in a channel), and the coordinator continues
// it with one activity per turn around its capabilities and the provider call, marked with the code when the turn fails. A stored context
// that cannot be read never holds the turn back.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantTraceTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_home_turn_keeps_the_trace_of_its_request_and_the_coordinator_continues_it_around_the_provider()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var trace = ActivityTraceId.CreateRandom();
        var requestId = Guid.NewGuid();
        await SubmitAsync(scenario, requestId, trace);
        var turn = await FindAsync(scenario, requestId);
        Assert.StartsWith($"00-{trace.ToHexString()}-", turn.TraceParent, StringComparison.Ordinal);

        Assert.True(await scenario.ProcessNextAsync());

        var activity = Assert.Single(stopped, item => item.OperationName == "ai.assistant.turn" && item.TraceId == trace);
        Assert.Equal(turn.TraceParent!.Split('-')[2], activity.ParentSpanId.ToHexString());
        Assert.Equal(turn.Id.ToString(), activity.GetTagItem("ai.turn.id"));
        Assert.Equal("OpenAI", activity.GetTagItem("ai.provider"));
        Assert.Equal("home", activity.GetTagItem("ai.channel"));
        Assert.Equal("Interpret", activity.GetTagItem("ai.purpose"));
        Assert.Contains("users.summary@1", Assert.IsType<string[]>(activity.GetTagItem("ai.capabilities")));
        Assert.Equal(ActivityStatusCode.Unset, activity.Status);
        // The call to the provider and its answer happen inside the turn's trace.
        Assert.Equal([trace, trace], scenario.Handler.Traces);
    }

    [Fact]
    public async Task A_failed_turn_marks_its_activity_with_the_failure_code()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.Anthropic, "users.summary");
        scenario.Handler.FailWith = HttpStatusCode.Unauthorized;
        var trace = ActivityTraceId.CreateRandom();
        await SubmitAsync(scenario, Guid.NewGuid(), trace);

        Assert.True(await scenario.ProcessNextAsync());

        var activity = Assert.Single(stopped, item => item.OperationName == "ai.assistant.turn" && item.TraceId == trace);
        Assert.Equal((ActivityStatusCode.Error, "Ai.Assistant.ProviderCredentialRejected"), (activity.Status, activity.StatusDescription));
        Assert.Equal("Anthropic", activity.GetTagItem("ai.provider"));
    }

    [Fact]
    public async Task A_turn_the_engine_fails_when_it_runs_out_of_provider_steps_marks_its_activity_with_that_code()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary", services =>
            services.PostConfigure<AiAssistantOptions>(options => options.Limits = options.Limits with { ProviderSteps = 1 }));
        var trace = ActivityTraceId.CreateRandom();
        var requestId = Guid.NewGuid();
        await SubmitAsync(scenario, requestId, trace);

        // The provider asks for a capability, and the step that would close the turn is one more than its limit.
        Assert.True(await scenario.ProcessNextAsync());

        Assert.Equal((AiAssistantTurnState.Failed, "Ai.Assistant.LimitExceeded"), await OutcomeAsync(scenario, requestId));
        var activity = Assert.Single(stopped, item => item.OperationName == "ai.assistant.turn" && item.TraceId == trace);
        Assert.Equal((ActivityStatusCode.Error, "Ai.Assistant.LimitExceeded"), (activity.Status, activity.StatusDescription));
    }

    [Fact]
    public async Task A_turn_the_engine_fails_when_its_connection_changes_during_the_answer_marks_its_activity_with_that_code()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        scenario.Handler.BeforeAnswer = async call =>
        {
            if (call != 2) return;
            using var paused = await scenario.Client.PutAsJsonAsync("/api/ai/connection", new
            {
                requestId = Guid.NewGuid(), provider = "OpenAI", model = "fictional-model", credentialChange = "Keep", enabled = false,
                expectedRevision = scenario.ConnectionRevision,
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        };
        var trace = ActivityTraceId.CreateRandom();
        var requestId = Guid.NewGuid();
        await SubmitAsync(scenario, requestId, trace);

        // The answer arrives, and the closing finds the connection changed.
        Assert.True(await scenario.ProcessNextAsync());

        Assert.Equal((AiAssistantTurnState.Failed, "Ai.Assistant.ConnectionChanged"), await OutcomeAsync(scenario, requestId));
        var activity = Assert.Single(stopped, item => item.OperationName == "ai.assistant.turn" && item.TraceId == trace);
        Assert.Equal((ActivityStatusCode.Error, "Ai.Assistant.ConnectionChanged"), (activity.Status, activity.StatusDescription));
    }

    [Fact]
    public async Task A_stored_trace_that_cannot_be_read_never_holds_the_turn_back()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "How many users are there?");
        var turn = await FindAsync(scenario, requestId);
        await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<AiAssistantTurn>()
            .Where(item => item.Id == turn.Id).ExecuteUpdateAsync(update => update.SetProperty(item => item.TraceParent, "00-not-a-trace"), Ct));
        Activity.Current = null;

        Assert.True(await scenario.ProcessNextAsync());

        using var response = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
        Assert.Equal("completed", (await response.ReadJsonAsync()).GetProperty("state").GetString());
        // The turn's activity starts its own trace.
        var activity = Assert.Single(stopped, item => item.OperationName == "ai.assistant.turn" && Equals(item.GetTagItem("ai.turn.id"), turn.Id.ToString()));
        Assert.Equal(default, activity.ParentSpanId);
    }

    [Fact]
    public async Task An_operation_keeps_the_trace_it_is_submitted_in_and_none_without_one()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "whatsapp.no-reply", services =>
            services.PostConfigure<AiAssistantOptions>(options => options.ModelProfiles = options.ModelProfiles
                .Where(profile => profile.Provider == AiProvider.OpenAI && profile.Model == "fictional-model")
                .Select(profile => profile with { Id = "fixture-model", Version = 1 }).ToArray()));
        var actor = Guid.Parse(scenario.Client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        // The person's own conversation after a capture, as a channel submits it from its round.
        AssistantOperationRequest Operation() => new(Guid.NewGuid(), new AssistantChannelContext("whatsapp-assistant", actor, actor, "en", "UTC",
            new([], []), [new("whatsapp.offered-forms", 1), new("whatsapp.no-reply", 1), new("whatsapp.decline-form", 1)], [new("assistant_proposal", 1)])
            { InteractionMode = AssistantInteractionMode.Assistant, SourceRecordId = Guid.NewGuid(), ChannelConversationId = Guid.NewGuid() },
            "A form response was captured.", now.AddMinutes(-1), now.AddHours(1));
        var untraced = Operation();
        var traced = Operation();

        Activity.Current = null;
        await SubmitOperationAsync(scenario, untraced);
        string? round;
        using (var caller = new Activity("channel.round").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            round = caller.Id;
            await SubmitOperationAsync(scenario, traced);
        }

        Assert.Null((await FindAsync(scenario, untraced.RequestId)).TraceParent);
        Assert.Equal(round, (await FindAsync(scenario, traced.RequestId)).TraceParent);
    }

    private static ActivityListener Listen(ConcurrentBag<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is AiAssistantTracing.SourceName or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static async Task SubmitAsync(AiAssistantExecutionScenario scenario, Guid requestId, ActivityTraceId trace)
    {
        using var submit = new HttpRequestMessage(HttpMethod.Post, "/api/ai/assistant/turns")
        {
            Content = JsonContent.Create(new { requestId, question = "How many users are there?", expectedConnectionRevision = scenario.ConnectionRevision }),
        };
        submit.Headers.Add("traceparent", $"00-{trace.ToHexString()}-{ActivitySpanId.CreateRandom().ToHexString()}-01");
        using var accepted = await scenario.Client.SendAsync(submit, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
    }

    private static async Task SubmitOperationAsync(AiAssistantExecutionScenario scenario, AssistantOperationRequest request)
    {
        var accepted = await scenario.ScopeAsync(services => services.GetRequiredService<IAssistantOperations>().SubmitAsync(request, Ct));
        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
    }

    private static Task<TraceRow> FindAsync(AiAssistantExecutionScenario scenario, Guid requestId) =>
        scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<AiAssistantTurn>()
            .Where(item => item.RequestId == requestId).Select(item => new TraceRow(item.Id, item.TraceParent)).SingleAsync(Ct));

    private static Task<(AiAssistantTurnState, string?)> OutcomeAsync(AiAssistantExecutionScenario scenario, Guid requestId) =>
        scenario.ScopeAsync(async services =>
        {
            var row = await services.GetRequiredService<ApplicationDbContext>().Set<AiAssistantTurn>()
                .Where(item => item.RequestId == requestId).Select(item => new { item.State, item.FailureCode }).SingleAsync(Ct);
            return (row.State, row.FailureCode);
        });

    private sealed record TraceRow(Guid Id, string? TraceParent);
}
