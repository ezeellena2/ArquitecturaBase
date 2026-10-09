using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.SystemQueries;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantExecutionServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("users.summary", "{\"sql\":\"select * from users\"}")]
    [InlineData("users.summary", "{\"ownerUserId\":\"other\"}")]
    [InlineData("users.summary", "{\"url\":\"https://evil.example\"}")]
    [InlineData("users.summary", "{\"x\":1,\"x\":2}")]
    [InlineData("invented_tool", "{}")]
    public async Task Unknown_tools_and_extra_arguments_never_query_a_source(string tool, string arguments)
    {
        var host = new Host();
        var response = new LlmConversationResponse("tools", [new("call", tool, arguments)], null, null);
        var result = await host.Service.AdvanceAsync(host.Context(), response, Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
        Assert.Equal(0, host.Queries.Calls);
    }

    [Fact]
    public async Task A_revoked_source_permission_prevents_the_query_even_when_it_was_in_the_catalog()
    {
        var host = new Host();
        host.Permissions.Permissions[host.UserId] = [ArtificialIntelligencePermissions.AssistantUse];
        var response = new LlmConversationResponse("tools", [new("call", "users.summary", "{}")], null, null);
        var result = await host.Service.AdvanceAsync(host.Context(), response, Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
        Assert.Equal(0, host.Queries.Calls);
    }

    [Fact]
    public async Task Terminal_contract_rejects_duplicate_properties_before_publication()
    {
        var host = new Host();
        var response = new LlmConversationResponse("tools",
            [new("call", "assistant_answer", "{\"kind\":\"clarification\",\"text\":\"Which role?\",\"text\":\"unsafe\",\"evidenceIds\":[]}")], null, null);
        var result = await host.Service.AdvanceAsync(host.Context(), response, Ct);
        Assert.Equal(AiAssistantErrors.InvalidOutput, result.Error);
    }

    [Fact]
    public async Task Typed_queries_receive_server_identity_and_the_captured_turn_culture()
    {
        var host = new Host();
        var response = new LlmConversationResponse("tools", [new("call", "users.summary", "{}")], "opaque", new(10, 2));
        var result = await host.Service.AdvanceAsync(host.Context(), response, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(host.UserId, host.Queries.LastRequest!.ActorUserId);
        Assert.Equal("en", host.Queries.LastRequest.Culture);
        Assert.Equal("UTC", host.Queries.LastRequest.TimeZoneId);
        Assert.Single(result.Value.Context.Evidences);
        Assert.Equal("opaque", result.Value.Context.ProviderRequest.Continuation);
        Assert.Single(result.Value.Context.ProviderRequest.ToolResults);
    }

    [Fact]
    public async Task A_source_without_evidence_is_recorded_and_its_call_identifier_cannot_be_reused()
    {
        var host = new Host();
        host.Queries.WithoutEvidence = true;
        var response = new LlmConversationResponse("tools", [new("call", "users.summary", "{}")], "opaque", new(10, 2));
        var result = await host.Service.AdvanceAsync(host.Context(), response, Ct);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Context.Evidences);
        var tool = Assert.Single(result.Value.NewTools);
        Assert.Null(tool.Evidence);
        Assert.Equal("notConnected", tool.State);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, tool.ObservedAtUtc);
        var repeated = await host.Service.AdvanceAsync(result.Value.Context, response, Ct);
        Assert.Equal(AiAssistantErrors.InvalidOutput, repeated.Error);
        Assert.Equal(1, host.Queries.Calls);
    }

    [Fact]
    public async Task Provider_timeout_uses_the_remaining_deadline_and_the_injected_clock()
    {
        var host = new Host();
        host.Client.OnSend = (_, token) =>
        {
            host.Clock.Advance(TimeSpan.FromSeconds(4));
            Assert.True(token.IsCancellationRequested);
            return Task.FromResult<Result<LlmConversationResponse>>(AiAssistantErrors.ProviderUncertain);
        };
        var context = host.Context();
        context = context with { Claim = context.Claim with { DeadlineAtUtc = host.Clock.GetUtcNow().UtcDateTime.AddSeconds(3) } };
        var result = await host.Service.CallProviderAsync(context, Ct);
        Assert.Equal(AiAssistantErrors.ProviderUncertain, result.Error);
        Assert.Equal(1, host.Client.Calls);
    }

    [Fact]
    public async Task Source_latency_uses_the_injected_clock_and_only_a_bounded_tool_identifier()
    {
        var durations = new ConcurrentBag<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == "ArquitecturaBase.AiAssistant" && instrument.Name == "ai.assistant.tool.duration")
                current.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) =>
        {
            Assert.True(tags.Length == 1 && tags[0].Key == "tool" && Equals(tags[0].Value, "users.summary"));
            durations.Add(value);
        });
        listener.Start();
        var host = new Host();
        host.Queries.OnExecute = () => host.Clock.Advance(TimeSpan.FromSeconds(2));
        var result = await host.Service.AdvanceAsync(host.Context(), new("tools", [new("call", "users.summary", "{}")], null, null), Ct);
        Assert.True(result.IsSuccess);
        Assert.Contains(2d, durations);
    }

    private sealed class Host
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
        public FakePermissionService Permissions { get; } = new();
        public Queries Queries { get; } = new();
        public Client Client { get; } = new();
        public Guid UserId { get; }
        public AiAssistantExecutionService Service { get; }
        public Host()
        {
            var users = new InMemoryUserAccounts();
            UserId = users.AddUser("fiction@example.test").Id;
            Permissions.Permissions[UserId] = [ArtificialIntelligencePermissions.AssistantUse, "users.read"];
            var limits = new AiAssistantLimits();
            var guard = new AiAssistantAccessGuard(users, Permissions, [], limits, new FakeCurrentUser { UserId = UserId });
            Service = new AiAssistantExecutionService(null!, guard, [Queries], Queries, Client, limits, Clock,
                NullLogger<AiAssistantExecutionService>.Instance);
        }
        public AiAssistantExecutionContextResponse Context()
        {
            var conversation = AiConversation.Create(UserId, Clock.GetUtcNow().UtcDateTime);
            var turn = AiAssistantTurn.Create(UserId, conversation.Id, Guid.NewGuid(), 1, "cipher", "en", "UTC", AiProvider.OpenAI,
                "model", 1, Clock.GetUtcNow().UtcDateTime, Clock.GetUtcNow().UtcDateTime.AddDays(30));
            turn.Claim(Guid.NewGuid(), Clock.GetUtcNow().UtcDateTime.AddSeconds(90));
            var claim = new AiAssistantClaimResponse(turn.Id, UserId, conversation.Id, turn.RequestId, turn.Fence,
                turn.AttemptId!.Value, turn.DeadlineAtUtc!.Value, "en");
            var row = new AiAssistantTurnRow(turn.Id, UserId, conversation.Id, turn.RequestId, 1, turn.State, turn.Stage,
                turn.Revision, 1, [], "cipher", null, null, 1, "en", "UTC", turn.Provider, turn.Model, 1, turn.AttemptId,
                turn.Fence, turn.SubmittedAtUtc, turn.ContentExpiresAtUtc, turn.DeadlineAtUtc, null, 0, 0, 0, null, null, false, null);
            var provider = new LlmConversationRequest(AiProvider.OpenAI, "model", "fictional-key", "policy", [],
                [new("users.summary", "summary", "{}"), new("assistant_answer", "answer", "{}")], [], null, 1500, 262144, 131072);
            return new(claim, row, provider, [], [], 0, 0);
        }
    }
    private sealed class Queries : ISystemQuerySource, ISystemQueryExecutor
    {
        public int Calls { get; private set; }
        public SystemQueryRequest? LastRequest { get; private set; }
        public Action? OnExecute { get; set; }
        public bool WithoutEvidence { get; set; }
        public IReadOnlyList<SystemQueryDefinition> Definitions { get; } = [new("users.summary", 1, "summary", "{}", ["users.read"])];
        public Task<Result<SystemQueryResponse>> ExecuteAsync(SystemQueryRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            OnExecute?.Invoke();
            LastRequest = request;
            if (WithoutEvidence) return Task.FromResult<Result<SystemQueryResponse>>(new SystemQueryResponse("notConnected", null));
            return Task.FromResult<Result<SystemQueryResponse>>(new SystemQueryResponse("complete", new(Guid.NewGuid(), "users.summary", 1,
                new DateTime(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc), null, "users", ["users.read"], [new(null, [new("Active", "10")])])));
        }
    }
    private sealed class Client : ILlmConversationClient
    {
        public int Calls { get; private set; }
        public Func<LlmConversationRequest, CancellationToken, Task<Result<LlmConversationResponse>>>? OnSend { get; set; }
        public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            return OnSend!(request, cancellationToken);
        }
    }
}
