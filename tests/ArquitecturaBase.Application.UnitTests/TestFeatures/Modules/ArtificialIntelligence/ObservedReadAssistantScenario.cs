using ArquitecturaBase.Application.Channels;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed record ObservedReadAssistantStorePorts(IAiAssistantRepository Repository, IAiConnectionRepository ConnectionRepository,
    IAiConnectionReader ConnectionReader, AiConversation Conversation, AiAssistantTurn Turn, IReadOnlyList<AiAssistantToolExecution> Tools);

internal sealed class ObservedReadAssistantScenario
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero));
    public FakePermissionService Permissions { get; } = new();
    public FakeUnitOfWork Work { get; } = new();
    public ObservedReadAssistantSource Source { get; } = new();
    public RoundtripAssistantProtector Protector { get; } = new();
    public ObservedReadAssistantStorePorts Store { get; }
    public AiAssistantExecutionProfileSnapshot Profile { get; }
    public AiAssistantClaimResponse Claim { get; }
    public AiAssistantExecutionService Execution { get; }
    public AiAssistantDispatchService Dispatch { get; }

    public ObservedReadAssistantScenario(Func<Guid, DateTime, string, ObservedReadAssistantStorePorts> createStore)
    {
        var users = new InMemoryUserAccounts();
        var actor = users.AddUser("observed-resource-fiction@example.test");
        Permissions.Permissions[actor.Id] = [];
        Store = createStore(actor.Id, Clock.GetUtcNow().UtcDateTime, "fixture-observed");
        Profile = new("observed-fixture", 1, "capabilities-v1", 1,
            new(Store.Conversation.ChannelId, actor.Id, null, Store.Turn.Culture, Store.Turn.TimeZoneId, new([], []),
                [new(ObservedReadAssistantSource.ReadId, 1), new(ObservedReadAssistantSource.ProposalId, 1)], [new("assistant_proposal", 1)]));
        var catalog = new AiAssistantCapabilityCatalogPolicy([Source], Permissions);
        var limits = new AiAssistantLimits();
        var guard = new AiAssistantAccessGuard(users, Permissions,
            [new AiAssistantModelProfile(AiProvider.OpenAI, "model", limits.ContextBytes, true, true) { Id = "observed-fixture", Version = 1 }],
            limits, new FakeCurrentUser { UserId = Guid.NewGuid() }, catalog, []);
        if (Store.Turn.SealExecutionProfile(JsonSerializer.Serialize(Profile, AiAssistantResponsePolicy.JsonOptions)).IsFailure)
            throw new InvalidOperationException("The observed-read fixture profile must be sealed before claiming.");
        Store.Turn.ProtectQuestion(Protector.Protect(actor.Id, Store.Conversation.Id, Store.Turn.Id, "question", 1, "Fictional resource request"));
        if (Store.Turn.Claim(Guid.NewGuid(), Clock.GetUtcNow().UtcDateTime.AddSeconds(90)).IsFailure)
            throw new InvalidOperationException("The observed-read fixture must be claimable.");
        Claim = new(Store.Turn.Id, actor.Id, Store.Conversation.Id, Store.Turn.RequestId, Store.Turn.Fence,
            Store.Turn.AttemptId!.Value, Store.Turn.DeadlineAtUtc!.Value, Store.Turn.Culture);
        Execution = new(null!, guard, catalog, Source, new ForbiddenProvider(), limits, Clock,
            NullLogger<AiAssistantExecutionService>.Instance);
        Dispatch = new(Store.Repository, Work,
            new AiAssistantDispatchGuard(Store.Repository, Store.ConnectionRepository, Store.ConnectionReader, guard, catalog),
            Protector, limits, Clock, NullLogger<AiAssistantDispatchService>.Instance, new DisabledAssistantOperationCompletionSignal());
    }

    public AiAssistantTurnRow Row()
    {
        var turn = Store.Turn;
        return new(turn.Id, turn.OwnerUserId, turn.ConversationId, turn.RequestId, turn.Ordinal, turn.State, turn.Stage, turn.Revision,
            Store.Conversation.Revision, Store.Conversation.RequiredPermissions.ToArray(), turn.ProtectedQuestion, turn.ProtectedAnswer,
            turn.ProtectedEvidences, turn.PayloadRevision, turn.Culture, turn.TimeZoneId, turn.Provider, turn.Model, turn.ConnectionRevision,
            turn.AttemptId, turn.Fence, turn.SubmittedAtUtc, turn.ContentExpiresAtUtc, turn.DeadlineAtUtc, turn.CancelRequestedAtUtc,
            turn.ProviderSteps, turn.ToolCalls, turn.ToolBytes, turn.InputTokens, turn.OutputTokens, turn.UsageUnknown, turn.FailureCode)
        {
            ChannelId = Store.Conversation.ChannelId, ExecutionProfileJson = turn.ExecutionProfileJson,
            ToolResults = Store.Tools.Select(tool => new AiAssistantToolResultRow(tool.Ordinal, tool.ToolId, tool.Version, tool.ProtectedResult)).ToArray()
        };
    }

    public AiAssistantExecutionContextResponse Context() => new(Claim, Row(),
        new(AiProvider.OpenAI, "model", "fictional-key", "Fictional read/proposal policy", [],
            [new(ObservedReadAssistantSource.ReadId, "read", "{}"), new(ObservedReadAssistantSource.ProposalId, "proposal", "{}"),
                new("assistant_proposal", "select", "{}")], [], null, 1500, 262144, 131072), [], [], 0, 0) { ExecutionProfile = Profile };

    public AiAssistantResponsePolicy ReloadedPolicy(ObservedReadAssistantSource codec) =>
        new(Protector, Clock, new AiAssistantCapabilityCatalogPolicy([codec], Permissions));

    private sealed class ForbiddenProvider : ILlmConversationClient
    {
        public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Observed-read tests never contact a provider.");
    }
}
