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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

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
    public async Task A_log_of_the_worker_during_a_turn_names_the_turn_in_its_scope()
    {
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.Anthropic, "users.summary", services =>
            services.AddLogging(logging => logging.AddFakeLogging().AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace)));
        scenario.Handler.FailWith = HttpStatusCode.Unauthorized;
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId, "How many users are there?");
        var turn = await FindAsync(scenario, requestId);

        Assert.True(await scenario.ProcessNextAsync());

        var records = await scenario.ScopeAsync(services => Task.FromResult(services.GetFakeLogCollector().GetSnapshot()));
        var failed = Assert.Single(records, record => record.Id.Name == "LogProviderFailed");
        Assert.Contains(failed.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(scope => scope),
            pair => pair.Key == "AssistantTurnId" && Equals(pair.Value, turn.Id));
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
    public async Task Checking_for_a_cancellation_while_the_provider_answers_stays_out_of_the_turns_trace()
    {
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = Listen(stopped.Enqueue, "Npgsql");
        await using var scenario = await AiAssistantExecutionScenario.CreateAsync(factory, AiProvider.OpenAI, "users.summary");
        var called = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scenario.Handler.BeforeAnswer = async call =>
        {
            if (call != 1) return;
            called.SetResult();
            await answer.Task;
        };
        var trace = ActivityTraceId.CreateRandom();
        await SubmitAsync(scenario, Guid.NewGuid(), trace);

        var processing = scenario.ProcessNextAsync();
        Activity[] checks;
        try
        {
            await called.Task.WaitAsync(Ct);
            var held = stopped.Count;
            await WaitForChecksAsync(stopped, held, 3);
            checks = TurnQueries(stopped.Skip(held)).ToArray();
        }
        finally { answer.TrySetResult(); }
        Assert.True(await processing);

        // Every half second the coordinator asks the database, and a turn of a minute and a half would bury the call to the provider.
        Assert.DoesNotContain(checks, item => item.TraceId == trace);
        // The turn's own queries, such as reserving its step and closing it, stay in its trace.
        Assert.Contains(stopped, item => item.Source.Name == "Npgsql" && item.TraceId == trace);
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

    private static ActivityListener Listen(ConcurrentBag<Activity> stopped) => Listen(stopped.Add, "Microsoft.AspNetCore");

    private static ActivityListener Listen(Action<Activity> stopped, string otherSource)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AiAssistantTracing.SourceName || source.Name == otherSource,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    // While the provider answers, the clock moves half a second at a time until the coordinator has asked this many times whether the
    // turn was canceled.
    private async Task WaitForChecksAsync(ConcurrentQueue<Activity> stopped, int from, int checks)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        while (TurnQueries(stopped.Skip(from)).Count() < checks)
        {
            var seen = TurnQueries(stopped.Skip(from)).Count();
            factory.Clock.Advance(TimeSpan.FromMilliseconds(500));
            for (var wait = 0; wait < 50 && TurnQueries(stopped.Skip(from)).Count() == seen; wait++)
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    // A query that reads the turns' table, whatever trace it runs in.
    private static IEnumerable<Activity> TurnQueries(IEnumerable<Activity> activities) => activities.Where(item => item.Source.Name == "Npgsql" &&
        item.TagObjects.Any(tag => tag.Value is string text && text.Contains("\"AiAssistantTurns\"", StringComparison.Ordinal)));

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
