using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantDispatchServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Neutral_channels_reserve_using_the_owners_exact_permissions_without_the_Home_permission(bool automatic)
    {
        var host = new NeutralHost(automatic);
        Assert.DoesNotContain(ArtificialIntelligencePermissions.AssistantUse, host.Permissions.Permissions[host.ActorId]);
        var begun = await host.Service.BeginProviderStepAsync(host.Claim, Ct);
        Assert.True(begun.IsSuccess);
        Assert.True(begun.Value);
        Assert.Equal(1, host.Store.Turn.ProviderSteps);
        Assert.Equal(automatic ? host.ActorId : host.SubjectId, host.Profile.ChannelContext.SubjectUserId);
        Assert.Equal(automatic ? Array.Empty<string>() : new[] { "whatsapp.read", "whatsapp.send" }, host.Profile.ChannelContext.RequiredPermissions.Actor);
        Assert.Equal(new[] { "formrecords.create" }, host.Profile.ChannelContext.RequiredPermissions.Subject);
        Assert.Equal(0, host.Source.Executions);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("subject")]
    public async Task A_sealed_identity_revoked_between_steps_prevents_the_next_provider_reservation(string revokedIdentity)
    {
        var host = new NeutralHost(false);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        // The other identity can hold these permissions; it must never lend them to the revoked identity.
        if (revokedIdentity == "actor") host.Permissions.Permissions[host.SubjectId] = ["formrecords.create", "whatsapp.read", "whatsapp.send"];
        else host.Permissions.Permissions[host.ActorId] = ["whatsapp.read", "whatsapp.send", "formrecords.create"];
        host.Permissions.Permissions[revokedIdentity == "actor" ? host.ActorId : host.SubjectId] = [];
        var next = await host.Service.BeginProviderStepAsync(host.Claim, Ct);
        Assert.True(next.IsSuccess);
        Assert.False(next.Value);
        Assert.Equal(AiAssistantTurnState.Failed, host.Store.Turn.State);
        Assert.Equal(1, host.Store.Turn.ProviderSteps);
        Assert.Equal(0, host.Source.Executions);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("subject")]
    public async Task Publishing_a_proposal_revalidates_each_sealed_identity_and_keeps_known_usage(string revokedIdentity)
    {
        var host = new NeutralHost(false);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        var proposal = host.Proposal();
        // The other identity can hold these permissions; it must never lend them to the revoked identity.
        if (revokedIdentity == "actor") host.Permissions.Permissions[host.SubjectId] = ["formrecords.create", "whatsapp.read", "whatsapp.send"];
        else host.Permissions.Permissions[host.ActorId] = ["whatsapp.read", "whatsapp.send", "formrecords.create"];
        host.Permissions.Permissions[revokedIdentity == "actor" ? host.ActorId : host.SubjectId] = [];
        var completion = new AiAssistantAdvanceResponse(host.Context([proposal]), null, [], new(10, 3)) { Proposal = proposal };
        Assert.True((await host.Service.CompleteAsync(host.Claim, completion, Ct)).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Failed, host.Store.Turn.State);
        Assert.Null(host.Store.Turn.ProtectedAnswer);
        Assert.Equal(10, host.Store.Turn.InputTokens);
        Assert.Equal(3, host.Store.Turn.OutputTokens);
    }

    [Fact]
    public async Task A_typed_proposal_advances_records_selects_completes_and_decodes_in_a_new_reader_scope()
    {
        var host = new NeutralHost(false, grantHomeForLegacyRed: true);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        var progress = await host.Execution.AdvanceAsync(host.Context(),
            new("tools", [new("proposal-call", "fixture.proposal", "{}")], "opaque-test-continuation", new(10, 2)), Ct);
        Assert.True(progress.IsSuccess);
        var proposal = Assert.Single(progress.Value.Context.Proposals);
        Assert.Equal(host.Claim.TurnId, proposal.TurnId);
        Assert.Equal(new AssistantCapabilityReference("fixture.proposal", 1), proposal.Capability);
        var payload = Assert.IsType<RoundtripAssistantPayload>(proposal.Payload);
        Assert.Equal(host.Source.ResourceId, payload.ResourceId);
        Assert.NotNull(host.Source.LastRequest);
        Assert.Equal(host.ActorId, host.Source.LastRequest.Context.ActorUserId);
        Assert.Equal(host.SubjectId, host.Source.LastRequest.Context.SubjectUserId);
        Assert.Equal(host.Source.PrivateText, payload.PrivateText);
        using (var emitted = JsonDocument.Parse(Assert.Single(progress.Value.Context.ProviderRequest.ToolResults).ResultJson))
        {
            Assert.Equal(proposal.Id, emitted.RootElement.GetProperty("proposalId").GetGuid());
            Assert.Equal(payload.ResourceId, emitted.RootElement.GetProperty("payload").GetProperty("resourceId").GetGuid());
            Assert.Equal(payload.PrivateText, emitted.RootElement.GetProperty("payload").GetProperty("privateText").GetString());
        }
        Assert.True((await host.Service.RecordProgressAsync(host.Claim, progress.Value, Ct)).IsSuccess);
        var durableTool = Assert.Single(host.Store.Tools);
        Assert.DoesNotContain(payload.PrivateText, durableTool.ProtectedResult!, StringComparison.Ordinal);
        var selected = await host.Execution.AdvanceAsync(progress.Value.Context,
            new("tools", [new("terminal", "assistant_proposal", JsonSerializer.Serialize(new { proposalId = proposal.Id }))], null, new(4, 1)), Ct);
        Assert.True(selected.IsSuccess);
        Assert.Null(selected.Value.Answer);
        Assert.NotNull(selected.Value.Proposal);
        Assert.Equal(proposal.Id, selected.Value.Proposal.Id);
        Assert.True((await host.Service.CompleteAsync(host.Claim, selected.Value, Ct)).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Completed, host.Store.Turn.State);
        var reloadedCodec = new RoundtripAssistantSource();
        var read = new AiAssistantResponsePolicy(host.Protector, host.Clock, new AiAssistantCapabilityCatalogPolicy([reloadedCodec], host.Permissions)).Map(host.Row(), true, 1);
        Assert.True(read.IsSuccess);
        Assert.Null(read.Value.Answer);
        Assert.NotNull(read.Value.Proposal);
        var reloaded = Assert.IsType<RoundtripAssistantPayload>(read.Value.Proposal.Payload);
        Assert.Equal(payload, reloaded);
        Assert.Equal(1, reloadedCodec.ParseCalls);
        Assert.Equal(0, reloadedCodec.Executions);
        Assert.Equal(1, host.Source.Executions);
        Assert.True((await host.Service.CompleteAsync(host.Claim, selected.Value, Ct)).IsSuccess);
        Assert.Single(host.Store.Tools);
        Assert.Equal(1, host.Source.Executions);
    }

    [Fact]
    public async Task A_neutral_channel_answer_roundtrips_through_the_same_completion_and_reader()
    {
        var host = new NeutralHost(false, answerTerminal: true);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        var advanced = await host.Execution.AdvanceAsync(host.Context(), new("tools",
            [new("answer-call", "assistant_answer", "{\"kind\":\"clarification\",\"text\":\"Please clarify your request.\",\"evidenceIds\":[]}")], null, new(4, 2)), Ct);
        Assert.True(advanced.IsSuccess);
        Assert.NotNull(advanced.Value.Answer);
        Assert.Null(advanced.Value.Proposal);
        Assert.True((await host.Service.CompleteAsync(host.Claim, advanced.Value, Ct)).IsSuccess);
        var mapped = new AiAssistantResponsePolicy(host.Protector, host.Clock,
            new AiAssistantCapabilityCatalogPolicy([host.Source], host.Permissions)).Map(host.Row(), true, 1);
        Assert.True(mapped.IsSuccess);
        Assert.NotNull(mapped.Value.Answer);
        Assert.Equal(advanced.Value.Answer.Kind, mapped.Value.Answer.Kind);
        Assert.Equal(advanced.Value.Answer.Text, mapped.Value.Answer.Text);
        Assert.Equal(advanced.Value.Answer.EvidenceIds, mapped.Value.Answer.EvidenceIds);
        Assert.Null(mapped.Value.Proposal);
        Assert.Empty(host.Store.Tools);
    }

    [Fact]
    public async Task A_known_result_after_owner_expiration_keeps_usage_without_persisting_new_protected_tools()
    {
        var host = new NeutralHost(false, ownerExpirySeconds: 5);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        var progress = await host.Execution.AdvanceAsync(host.Context(),
            new("tools", [new("proposal-call", "fixture.proposal", "{}")], null, new(10, 3)), Ct);
        Assert.True(progress.IsSuccess);
        var proposal = Assert.Single(progress.Value.Context.Proposals);
        host.Clock.Advance(TimeSpan.FromSeconds(5));
        var result = await host.Service.CompleteAsync(host.Claim, progress.Value with { Proposal = proposal }, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(AiAssistantTurnState.Failed, host.Store.Turn.State);
        Assert.Equal(AiAssistantErrors.Expired.Code, host.Store.Turn.FailureCode);
        Assert.Empty(host.Store.Tools);
        Assert.Null(host.Store.Turn.ProtectedAnswer);
        Assert.Equal(10, host.Store.Turn.InputTokens);
        Assert.Equal(3, host.Store.Turn.OutputTokens);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("output")]
    [InlineData("items")]
    public async Task Capability_budgets_apply_in_the_real_Advance_path_before_publishing_content(string budget)
    {
        var host = new NeutralHost(false, budget: budget);
        var result = await host.Execution.AdvanceAsync(host.Context(), new("tools", [new("proposal", "fixture.proposal", "{}")], null, null), Ct);
        Assert.Equal(AiAssistantErrors.LimitExceeded, result.Error);
        if (budget == "input") Assert.Equal(0, host.Source.Executions);
        Assert.Empty(host.Store.Tools);
    }

    [Fact]
    public async Task A_capability_timeout_uses_its_own_smaller_budget_and_the_existing_clock()
    {
        var host = new NeutralHost(false, budget: "timeout");
        host.Source.BeforeReturn = token =>
        {
            host.Clock.Advance(TimeSpan.FromSeconds(2));
            Assert.True(token.IsCancellationRequested);
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.Execution.AdvanceAsync(host.Context(),
            new("tools", [new("proposal", "fixture.proposal", "{}")], null, null), Ct));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("wrong-type")]
    [InlineData("wrong-version")]
    public void Protected_proposals_are_decoded_by_the_exact_owner_codec_with_closed_typed_content(string variant)
    {
        var host = new NeutralHost(false, grantHomeForLegacyRed: true);
        var proposal = host.Proposal();
        var payloadJson = JsonSerializer.Serialize(new { resourceId = host.Source.ResourceId, privateText = host.Source.PrivateText });
        if (variant == "extra") payloadJson = payloadJson[..^1] + ",\"operatorUserId\":\"untrusted\"}";
        if (variant == "duplicate") payloadJson = payloadJson[..^1] + ",\"privateText\":\"replacement\"}";
        if (variant == "wrong-type") payloadJson = "{\"resourceId\":1,\"privateText\":false}";
        var version = variant == "wrong-version" ? 2 : 1;
        var output = "{\"state\":\"proposed\",\"proposalId\":\"" + proposal.Id + "\",\"payload\":" + payloadJson + "}";
        var toolCipher = host.Protector.Protect(host.ActorId, host.Store.Conversation.Id, host.Claim.TurnId, "tool:1", 1,
            "{\"output\":" + output + ",\"readOrdinals\":[]}");
        host.Store.Tools.Add(AiAssistantToolExecution.Create(host.Claim.TurnId, 1, "proposal", "fixture.proposal", version, [], "", host.Clock.GetUtcNow().UtcDateTime, toolCipher, 100));
        var protectedResult = host.Protector.Protect(host.ActorId, host.Store.Conversation.Id, host.Claim.TurnId, "answer", 1, "{\"kind\":\"proposal\",\"ordinal\":1}");
        var protectedEvidence = host.Protector.Protect(host.ActorId, host.Store.Conversation.Id, host.Claim.TurnId, "evidences", 1, "[]");
        Assert.True(host.Store.Turn.Complete(host.Claim.Fence, protectedResult, protectedEvidence, host.Clock.GetUtcNow().UtcDateTime).IsSuccess);
        var codec = new RoundtripAssistantSource();
        var read = new AiAssistantResponsePolicy(host.Protector, host.Clock, new AiAssistantCapabilityCatalogPolicy([codec], host.Permissions)).Map(host.Row(), true, 1);
        if (variant == "valid")
        {
            Assert.True(read.IsSuccess);
            Assert.NotNull(read.Value.Proposal);
            Assert.Equal(new RoundtripAssistantPayload(host.Source.ResourceId, host.Source.PrivateText), Assert.IsType<RoundtripAssistantPayload>(read.Value.Proposal.Payload));
            Assert.Equal(1, codec.ParseCalls);
        }
        else Assert.True(read.IsFailure);
        Assert.Equal(0, codec.Executions);
    }

    private sealed class NeutralHost
    {
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero));
        public FakePermissionService Permissions { get; } = new();
        public FakeUnitOfWork Work { get; } = new();
        public RecordingAssistantOperationCompletionSignal CompletionSignal { get; } = new();
        public RoundtripAssistantSource Source { get; } = new();
        public RoundtripAssistantProtector Protector { get; } = new();
        public Store Store { get; }
        public Guid ActorId { get; }
        public Guid SubjectId { get; }
        public AiAssistantExecutionProfileSnapshot Profile { get; }
        public AiAssistantClaimResponse Claim { get; }
        public AiAssistantDispatchService Service { get; }
        public AiAssistantExecutionService Execution { get; }
        public NeutralHost(bool automatic, bool grantHomeForLegacyRed = false, string? budget = null, bool answerTerminal = false, int? ownerExpirySeconds = null)
        {
            if (budget == "input") Source.Budget = Source.Budget with { InputBytes = 1 };
            if (budget == "output") { Source.Budget = Source.Budget with { OutputBytes = 100 }; Source.PrivateText = new string('x', 200); }
            if (budget == "items") Source.ItemCount = 6;
            if (budget == "timeout") Source.Budget = Source.Budget with { TimeoutSeconds = 1 };
            var users = new InMemoryUserAccounts();
            ActorId = users.AddUser("operator-fiction@example.test").Id;
            SubjectId = automatic ? ActorId : users.AddUser("subject-fiction@example.test").Id;
            Permissions.Permissions[ActorId] = automatic ? ["formrecords.create"] : ["whatsapp.read", "whatsapp.send"];
            Permissions.Permissions[SubjectId] = automatic ? ["formrecords.create"] : ["formrecords.create"];
            if (grantHomeForLegacyRed) Permissions.Permissions[ActorId] = [.. Permissions.Permissions[ActorId], ArtificialIntelligencePermissions.AssistantUse];
            var channel = automatic ? "fixture-automatic" : "fixture-manual";
            Profile = new("fixture-model", 1, "capabilities-v1", 1,
                new(channel, ActorId, SubjectId, "en", "UTC", new(automatic ? [] : ["whatsapp.read", "whatsapp.send"], ["formrecords.create"]),
                    [new("fixture.proposal", 1)], [new(answerTerminal ? "assistant_answer" : "assistant_proposal", 1)]));
            var limits = new AiAssistantLimits();
            var catalog = new AiAssistantCapabilityCatalogPolicy([Source], Permissions);
            var guard = new AiAssistantAccessGuard(users, Permissions, [new AiAssistantModelProfile(AiProvider.OpenAI, "model", limits.ContextBytes, true, true) { Id = "fixture-model", Version = 1 }], limits,
                new FakeCurrentUser { UserId = Guid.NewGuid() }, catalog, []);
            Store = new Store(ActorId, Clock.GetUtcNow().UtcDateTime, channel, "en",
                ownerExpirySeconds is { } seconds ? Clock.GetUtcNow().UtcDateTime.AddSeconds(seconds) : null);
            Store.Turn.SealExecutionProfile(JsonSerializer.Serialize(Profile, AiAssistantResponsePolicy.JsonOptions));
            Store.Turn.ProtectQuestion(Protector.Protect(ActorId, Store.Conversation.Id, Store.Turn.Id, "question", 1, "Fictitious registration intent"));
            Assert.True(Store.Turn.Claim(Guid.NewGuid(), Clock.GetUtcNow().UtcDateTime.AddSeconds(90)).IsSuccess);
            Claim = new(Store.Turn.Id, ActorId, Store.Conversation.Id, Store.Turn.RequestId, Store.Turn.Fence,
                Store.Turn.AttemptId!.Value, Store.Turn.DeadlineAtUtc!.Value, "en");
            Service = new(Store, Work, new AiAssistantDispatchGuard(Store, Store, Store, guard, catalog),
                Protector, limits, Clock, NullLogger<AiAssistantDispatchService>.Instance, CompletionSignal);
            Execution = new(null!, guard, catalog, Source, new ForbiddenClient(), limits, Clock, NullLogger<AiAssistantExecutionService>.Instance);
        }
        public AssistantObservedProposal Proposal() => new(Claim.TurnId, Guid.NewGuid(), new("fixture.proposal", 1), new RoundtripAssistantPayload(Source.ResourceId, Source.PrivateText));
        public AiAssistantTurnRow Row()
        {
            var turn = Store.Turn;
            return new(turn.Id, ActorId, turn.ConversationId, turn.RequestId, 1, turn.State, turn.Stage, turn.Revision,
                Store.Conversation.Revision, [], turn.ProtectedQuestion, turn.ProtectedAnswer, turn.ProtectedEvidences, turn.PayloadRevision,
                "en", "UTC", turn.Provider, turn.Model, turn.ConnectionRevision, turn.AttemptId, turn.Fence, turn.SubmittedAtUtc,
                turn.ContentExpiresAtUtc, turn.DeadlineAtUtc, turn.CancelRequestedAtUtc, turn.ProviderSteps, turn.ToolCalls, turn.ToolBytes,
                turn.InputTokens, turn.OutputTokens, turn.UsageUnknown, turn.FailureCode) { ChannelId = Store.Conversation.ChannelId, ExecutionProfileJson = turn.ExecutionProfileJson,
                    ToolResults = Store.Tools.Select(tool => new AiAssistantToolResultRow(tool.Ordinal, tool.ToolId, tool.Version, tool.ProtectedResult)).ToArray() };
        }
        public AiAssistantExecutionContextResponse Context(IReadOnlyList<AssistantObservedProposal>? proposals = null) =>
            new(Claim, Row(), new(AiProvider.OpenAI, "model", "fictional-key", "fixture policy", [],
                [new("fixture.proposal", "proposal", "{}"), new(Profile.ChannelContext.AllowedTerminals[0].Id, "terminal", "{}")], [], null, 1500, 262144, 131072), [], [], 0, 0)
            { ExecutionProfile = Profile, Proposals = proposals ?? [] };
    }
    private sealed class ForbiddenClient : ILlmConversationClient
    {
        public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Deterministic Advance tests do not contact a provider.");
    }
}
