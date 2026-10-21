using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantDispatchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("connection", "Ai.Assistant.ConnectionChanged", AiAssistantTurnState.Failed)]
    [InlineData("permission", "Ai.Assistant.ContentBlocked", AiAssistantTurnState.Failed)]
    [InlineData("cancellation", "Ai.Assistant.Canceled", AiAssistantTurnState.Canceled)]
    public async Task Publication_rechecks_current_access_and_preserves_known_provider_usage(string change, string expectedCode, AiAssistantTurnState expectedState)
    {
        var host = new Host();
        host.Store.Turn.BeginProviderStep(host.Claim.Fence, host.Clock.GetUtcNow().UtcDateTime, 4);
        if (change == "connection") host.Store.ConnectionRevision = 2;
        if (change == "permission") host.Permissions.Permissions[host.Store.Turn.OwnerUserId] = [ArtificialIntelligencePermissions.AssistantUse];
        if (change == "cancellation") host.Store.Turn.RequestCancellation(host.Clock.GetUtcNow().UtcDateTime);
        var completion = new AiAssistantAdvanceResponse(host.Context(), new("answer", "Confirmed source data.", [host.Evidence.Id]), [], new(10, 3));
        var result = await host.Service.CompleteAsync(host.Claim, completion, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(expectedState, host.Store.Turn.State);
        Assert.Equal(expectedCode, host.Store.Turn.FailureCode);
        Assert.Null(host.Store.Turn.ProtectedAnswer);
        Assert.Equal(10, host.Store.Turn.InputTokens);
        Assert.Equal(3, host.Store.Turn.OutputTokens);
        Assert.False(host.Store.Turn.UsageUnknown);
        Assert.Equal(1, host.Work.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, host.Work.LastPolicy);
    }

    [Fact]
    public async Task A_revoked_Home_source_permission_remains_content_blocked_when_the_permission_was_sealed_at_acceptance()
    {
        var host = new Host(sealCapabilityPermissions: true);
        host.Store.Turn.BeginProviderStep(host.Claim.Fence, host.Clock.GetUtcNow().UtcDateTime, 4);
        host.Permissions.Permissions[host.Store.Turn.OwnerUserId] = [ArtificialIntelligencePermissions.AssistantUse];
        var completion = new AiAssistantAdvanceResponse(host.Context(), new("answer", "Confirmed source data.", [host.Evidence.Id]), [], new(10, 3));
        Assert.True((await host.Service.CompleteAsync(host.Claim, completion, Ct)).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Failed, host.Store.Turn.State);
        Assert.Equal(AiAssistantErrors.ContentBlocked.Code, host.Store.Turn.FailureCode);
        Assert.Null(host.Store.Turn.ProtectedAnswer);
        Assert.Equal(10, host.Store.Turn.InputTokens);
    }

    [Fact]
    public async Task Reserving_an_external_step_commits_the_conservative_source_union_before_any_queries()
    {
        var host = new Host();
        var result = await host.Service.BeginProviderStepAsync(host.Claim, Ct);
        Assert.True(result.Value);
        Assert.Equal(new[] { "roles.read", "users.read" }, host.Store.Conversation.RequiredPermissions);
        Assert.Equal(1, host.Store.Turn.ProviderSteps);
        Assert.Equal(1, host.Work.Commits);
        Assert.Equal(0, host.Source.Executions);
    }

    [Fact]
    public async Task Recording_a_source_without_evidence_preserves_its_state_and_protected_result()
    {
        var host = new Host();
        var now = host.Clock.GetUtcNow().UtcDateTime;
        host.Store.Turn.BeginProviderStep(host.Claim.Fence, now, 4);
        var context = host.Context() with { ToolCalls = 1, ToolBytes = 42 };
        var observed = new AiAssistantObservedTool(1, "call", "users.summary", 1, ["users.read"], null,
            "{\"state\":\"notConnected\"}", 42) { State = "notConnected", ObservedAtUtc = now };
        var result = await host.Service.RecordProgressAsync(host.Claim, new(context, null, [observed], new(10, 2)), Ct);
        Assert.True(result.IsSuccess);
        var persisted = Assert.Single(host.Store.Tools);
        Assert.Equal("notConnected", persisted.State);
        Assert.Equal(string.Empty, persisted.EvidenceId);
        Assert.Equal(now, persisted.ObservedAtUtc);
        Assert.Equal("cipher", persisted.ProtectedResult);
        Assert.Equal(1, host.Store.Turn.ToolCalls);
    }

    [Fact]
    public async Task Purging_a_deleted_owners_content_also_hides_the_conversation_and_requests_cancellation()
    {
        var host = new Host();
        host.Store.Purgeable = true;
        var retention = new AiAssistantRetentionService(host.Store, host.Work, new AiAssistantLimits(), host.Clock,
            NullLogger<AiAssistantRetentionService>.Instance, new InMemoryUserAccounts());
        var result = await retention.PurgeBatchAsync(Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(AiConversationState.Removed, host.Store.Conversation.State);
        Assert.NotNull(host.Store.Turn.CancelRequestedAtUtc);
        Assert.Null(host.Store.Turn.ProtectedQuestion);
    }

    [Fact]
    public async Task Removed_conversations_are_physically_purged_only_after_the_content_and_receipt_window()
    {
        var host = new Host();
        host.Store.Conversation.Remove(host.Clock.GetUtcNow().UtcDateTime);
        host.Clock.Advance(TimeSpan.FromDays(31));
        host.Store.Turn.Reconcile(host.Clock.GetUtcNow().UtcDateTime, 120, 15);
        host.Store.Purgeable = true;
        host.Store.Removable = true;
        var retention = new AiAssistantRetentionService(host.Store, host.Work, new AiAssistantLimits(), host.Clock,
            NullLogger<AiAssistantRetentionService>.Instance, new InMemoryUserAccounts());
        var result = await retention.PurgeBatchAsync(Ct);
        Assert.True(result.IsSuccess);
        Assert.True(host.Store.PhysicallyRemoved);
        Assert.Equal(1, host.Work.Commits);
    }

    private sealed class Host
    {
        public InMemoryUserAccounts Users { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero));
        public FakePermissionService Permissions { get; } = new();
        public FakeUnitOfWork Work { get; } = new();
        public RecordingAssistantOperationCompletionSignal CompletionSignal { get; } = new();
        public Source Source { get; } = new();
        public Store Store { get; }
        public AiAssistantClaimResponse Claim { get; }
        public SystemQueryEvidence Evidence { get; }
        public AiAssistantDispatchService Service { get; }
        public AiAssistantExecutionProfileSnapshot Profile { get; }
        public Host(bool sealCapabilityPermissions = false)
        {
            var users = Users;
            var actor = users.AddUser("fictional@example.test");
            Permissions.Permissions[actor.Id] = [ArtificialIntelligencePermissions.AssistantUse, "users.read", "roles.read"];
            var limits = new AiAssistantLimits();
            var catalog = new AiAssistantCapabilityCatalogPolicy([Source], Permissions);
            var guard = new AiAssistantAccessGuard(users, Permissions, [new(AiProvider.OpenAI, "model", 131072, true, true) {Id="home"}], limits,
                new FakeCurrentUser { UserId = actor.Id }, catalog, []);
            Store = new Store(actor.Id, Clock.GetUtcNow().UtcDateTime);
            Profile = new("home", 1, "home-system", 1,
                new("home", actor.Id, null, "es", "UTC", new(sealCapabilityPermissions
                        ? [ArtificialIntelligencePermissions.AssistantUse, "users.read", "roles.read"] : [ArtificialIntelligencePermissions.AssistantUse], []),
                    [new("users.summary", 1), new("roles.list", 1)], [new("assistant_answer", 1)]));
            Store.Turn.SealExecutionProfile(JsonSerializer.Serialize(Profile, AiAssistantResponsePolicy.JsonOptions));
            Store.Conversation.RequirePermissions(["users.read"]);
            Store.Turn.Claim(Guid.NewGuid(), Clock.GetUtcNow().UtcDateTime.AddSeconds(90));
            Claim = new(Store.Turn.Id, actor.Id, Store.Conversation.Id, Store.Turn.RequestId, Store.Turn.Fence,
                Store.Turn.AttemptId!.Value, Store.Turn.DeadlineAtUtc!.Value, "es");
            Evidence = new(Guid.NewGuid(), "users.summary", 1, Clock.GetUtcNow().UtcDateTime, null, "users", ["users.read"], []);
            Service = new AiAssistantDispatchService(Store, Work, new AiAssistantDispatchGuard(Store, Store, Store, guard, catalog),
                new Protector(), limits, Clock, NullLogger<AiAssistantDispatchService>.Instance, CompletionSignal);
        }
        public AiAssistantExecutionContextResponse Context()
        {
            var turn = Store.Turn;
            var row = new AiAssistantTurnRow(turn.Id, turn.OwnerUserId, turn.ConversationId, turn.RequestId, 1, turn.State,
                turn.Stage, turn.Revision, 1, ["users.read"], "cipher", null, null, 1, "es", "UTC", turn.Provider, "model", 1,
                turn.AttemptId, turn.Fence, turn.SubmittedAtUtc, turn.ContentExpiresAtUtc, turn.DeadlineAtUtc,
                turn.CancelRequestedAtUtc, turn.ProviderSteps, 0, 0, null, null, true, null) { ExecutionProfileJson = turn.ExecutionProfileJson };
            return new(Claim, row, new(AiProvider.OpenAI, "model", "fake-key", "policy", [], [], [], null, 1500, 262144, 131072),
                [Evidence], [], 0, 0) { ExecutionProfile = Profile };
        }
    }
    private sealed class Protector : IAiConversationProtector
    {
        public string Protect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string plaintext) => "cipher";
        public string? Unprotect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string ciphertext) => null;
    }
    private sealed class Source : IAssistantCapabilitySource
    {
        public int Executions { get; private set; }
        public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
            [Definition("users.summary", "summary", "users.read"), Definition("roles.list", "roles", "roles.read")];
        private static AssistantCapabilityDefinition Definition(string id, string description, string permission) => new(id, 1,
            AssistantCapabilityKind.Read, description,
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
            SystemQueryCapabilityCodec.OutputSchemaJson, new([permission], []), new(4096, 16384, 50, 5));
        public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
        {
            Executions++;
            throw new InvalidOperationException("Reservation cannot query a source.");
        }
        public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
            throw new InvalidOperationException("Reservation cannot parse capability output.");
    }
    private sealed class Store : IAiAssistantRepository, IAiConnectionRepository, IAiConnectionReader
    {
        public AiConversation Conversation { get; }
        public AiAssistantTurn Turn { get; }
        public long ConnectionRevision { get; set; } = 1;
        public bool Purgeable { get; set; }
        public bool Removable { get; set; }
        public bool PhysicallyRemoved { get; private set; }
        public AiAssistantRequestReceipt? Receipt { get; set; }
        public AiAssistantTurn? ActiveForConversation { get; set; }
        public List<AiAssistantToolExecution> Tools { get; } = [];
        public Store(Guid actor, DateTime nowUtc, string channelId = "home", string culture = "es", DateTime? contentExpiresAtUtc = null)
        {
            Conversation = AiConversation.Create(actor, nowUtc, channelId);
            Turn = AiAssistantTurn.Create(actor, Conversation.Id, Guid.NewGuid(), 1, "cipher", culture, "UTC", AiProvider.OpenAI,
                "model", 1, nowUtc, contentExpiresAtUtc ?? nowUtc.AddDays(30), channelId);
        }
        public Task LockBudgetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LockActorAsync(Guid ownerUserId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LockConversationAsync(Guid conversationId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task LockAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AiAssistantTurn?> GetTurnAsync(Guid turnId, CancellationToken cancellationToken) => Task.FromResult<AiAssistantTurn?>(Turn);
        public Task<AiConversation?> GetConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) => Task.FromResult<AiConversation?>(Conversation);
        public Task<AiConnectionSnapshot> FindAsync(CancellationToken cancellationToken) => Task.FromResult(new AiConnectionSnapshot(AiProvider.OpenAI, "model", "cipher-key", true, ConnectionRevision));
        public Task<AiAssistantRequestReceipt?> GetReceiptAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Receipt is { } receipt && receipt.OwnerUserId == ownerUserId && receipt.RequestId == requestId ? receipt : null);
        public Task<AiAssistantTurn?> GetPendingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurn?> GetActiveForActorChannelAsync(Guid ownerUserId, string channelId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurn?> GetActiveForConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(ActiveForConversation is { } active && active.ConversationId == conversationId ? active : null);
        public Task<int> CountTurnsAsync(Guid conversationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantBudgetCounts> CountBudgetAsync(Guid ownerUserId, DateOnly dayUtc, string? channelId, bool? isAudio, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiAssistantTurn>> ListExpiredAsync(DateTime nowUtc, int pendingSeconds, int graceSeconds, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiAssistantTurn>> ListPurgeableAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiAssistantTurn>>(Purgeable ? [Turn] : []);
        public Task<IReadOnlyList<AiConversation>> ListRemovableConversationsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiConversation>>(Removable ? [Conversation] : []);
        public void RemoveConversation(AiConversation conversation) => PhysicallyRemoved = true;
        public Task<IReadOnlyList<AiAssistantRequestReceipt>> ListExpiredReceiptsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiAssistantRequestReceipt>>([]);
        public Task<IReadOnlyList<AiAssistantToolExecution>> ListToolsAsync(Guid turnId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiAssistantToolExecution>>(Tools);
        public void AddConversation(AiConversation conversation) => throw new NotSupportedException();
        public void AddTurn(AiAssistantTurn turn) => throw new NotSupportedException();
        public void AddReceipt(AiAssistantRequestReceipt receipt) => throw new NotSupportedException();
        public void AddTool(AiAssistantToolExecution execution) => Tools.Add(execution);
        public void RemoveTool(AiAssistantToolExecution execution) => throw new NotSupportedException();
        public void RemoveReceipt(AiAssistantRequestReceipt receipt) => throw new NotSupportedException();
        public Task<AiConnection?> GetAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiConnectionChange?> GetChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void Add(AiConnection connection) => throw new NotSupportedException();
        public void AddChange(AiConnectionChange change) => throw new NotSupportedException();
        public Task DeleteExpiredChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiConnectionChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
