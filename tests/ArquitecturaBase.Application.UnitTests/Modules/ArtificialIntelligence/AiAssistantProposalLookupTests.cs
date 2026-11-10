using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantProposalLookupTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly string[] Group = [ObservedReadAssistantSource.ProposalId];

    private static async Task<(ObservedReadAssistantScenario Scenario, Guid ProposalId)> CompletedAsync(bool home = true)
    {
        var scenario = new ObservedReadAssistantScenario(AiAssistantDispatchServiceTests.CreateObservedReadStore, home);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), new("tools", [new("read-call", ObservedReadAssistantSource.ReadId, "{}")], "read", null), Ct);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, read.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var proposal = await scenario.Execution.AdvanceAsync(read.Value.Context, new("tools",
            [new("proposal-call", ObservedReadAssistantSource.ProposalId, JsonSerializer.Serialize(new { resourceId = scenario.Source.ResourceId }))], "proposal", null), Ct);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, proposal.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var observed = Assert.Single(proposal.Value.Context.Proposals);
        var closing = home
            ? new LlmConversationResponse("tools", [new("answer-call", "assistant_answer", JsonSerializer.Serialize(new { kind = "proposal", text = "Listo.", evidenceIds = Array.Empty<Guid>(), proposalId = observed.Id }))], null, null)
            : new LlmConversationResponse("tools", [new("select-call", "assistant_proposal", JsonSerializer.Serialize(new { proposalId = observed.Id }))], null, null);
        var closed = await scenario.Execution.AdvanceAsync(proposal.Value.Context, closing, Ct);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, closed.Value, Ct)).IsSuccess);
        return (scenario, observed.Id);
    }

    private static AiAssistantProposalLookup Lookup(ObservedReadAssistantScenario scenario, Reader reader) =>
        new(reader, scenario.Guard, scenario.ReloadedPolicy(new ObservedReadAssistantSource()), NullLogger<AiAssistantProposalLookup>.Instance);

    [Fact]
    public async Task A_Home_proposal_is_found_with_the_turn_time_as_its_provenance()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var row = scenario.Row();
        var result = await Lookup(scenario, new Reader(row, [row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(proposalId, result.Value.Proposal.Id);
        Assert.Equal(("home", row.SubmittedAtUtc), (result.Value.Provenance.ChannelId, result.Value.Provenance.OccurredAtUtc));
        Assert.Equal(("home-system", 2), (result.Value.Provenance.InstructionsId, result.Value.Provenance.InstructionsVersion));
        Assert.True(result.Value.IsCurrent);
    }

    [Fact]
    public async Task A_later_proposal_of_the_same_capability_replaces_it()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var row = scenario.Row();
        var later = row with { Id = Guid.NewGuid(), Ordinal = row.Ordinal + 1 };
        var result = await Lookup(scenario, new Reader(row, [later, row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.False(result.Value.IsCurrent);
    }

    // Stage 2: capabilities that replace each other form a group; a proposal outside it keeps the rule of its own capability.
    [Fact]
    public async Task A_later_proposal_of_another_capability_replaces_it_only_within_its_group()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var row = scenario.Row();
        var later = row with { Id = Guid.NewGuid(), Ordinal = row.Ordinal + 1, ToolResults = row.ToolResults
            .Select(tool => tool.ToolId == ObservedReadAssistantSource.ProposalId ? tool with { ToolId = ReplacementSource.Id } : tool).ToArray() };
        var lookup = new AiAssistantProposalLookup(new Reader(row, [later, row]), scenario.Guard, new AiAssistantResponsePolicy(scenario.Protector,
            scenario.Clock, new AiAssistantCapabilityCatalogPolicy([new ObservedReadAssistantSource(), new ReplacementSource()], scenario.Permissions)),
            NullLogger<AiAssistantProposalLookup>.Instance);
        Assert.True((await lookup.FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct)).Value.IsCurrent);
        Assert.False((await lookup.FindAsync(row.OwnerUserId, row.RequestId, proposalId, [ObservedReadAssistantSource.ProposalId, ReplacementSource.Id], Ct)).Value.IsCurrent);
        Assert.True((await lookup.FindAsync(row.OwnerUserId, row.RequestId, proposalId, [ReplacementSource.Id], Ct)).Value.IsCurrent);
    }

    [Fact]
    public async Task A_later_answer_or_an_unfinished_turn_keeps_it_current()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var row = scenario.Row();
        var answer = scenario.Protector.Protect(row.OwnerUserId, row.ConversationId, row.Id, "answer", 1,
            JsonSerializer.Serialize(new AiAssistantAnswer("answer", "Hay 12.", []), JsonSerializerOptions.Web));
        var text = row with { Id = Guid.NewGuid(), Ordinal = row.Ordinal + 1, ProtectedAnswer = answer, ToolResults = [] };
        var failed = row with { Id = Guid.NewGuid(), Ordinal = row.Ordinal + 2, State = AiAssistantTurnState.Failed };
        var result = await Lookup(scenario, new Reader(row, [failed, text, row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.True(result.Value.IsCurrent);
    }

    [Fact]
    public async Task Another_proposal_id_or_a_channel_turn_is_not_found()
    {
        var (home, proposalId) = await CompletedAsync();
        var row = home.Row();
        Assert.Equal(AiAssistantErrors.NotFound, (await Lookup(home, new Reader(row, [row])).FindAsync(row.OwnerUserId, row.RequestId, Guid.NewGuid(), Group, Ct)).Error);
        var (channel, channelProposal) = await CompletedAsync(home: false);
        var channelRow = channel.Row();
        Assert.Equal(AiAssistantErrors.NotFound, (await Lookup(channel, new Reader(channelRow, [channelRow])).FindAsync(channelRow.OwnerUserId, channelRow.RequestId, channelProposal, Group, Ct)).Error);
    }

    [Fact]
    public async Task An_expired_proposal_is_not_found()
    {
        var (scenario, proposalId) = await CompletedAsync();
        scenario.Clock.Advance(TimeSpan.FromDays(400));
        var row = scenario.Row();
        var result = await Lookup(scenario, new Reader(row, [row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.Equal(AiAssistantErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Losing_the_Home_permission_blocks_it_before_reading_the_proposal()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var row = scenario.Row();
        scenario.Permissions.Permissions[row.OwnerUserId] = [];
        var result = await Lookup(scenario, new Reader(row, [row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.True(result.IsFailure);
    }

    // Another proposal capability that reads the same payload as the fixture's.
    private sealed class ReplacementSource : IAssistantCapabilitySource
    {
        public const string Id = "fixture.observed-replacement";
        private readonly ObservedReadAssistantSource codec = new();
        public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = [new ObservedReadAssistantSource().Definitions[1] with { Id = Id }];
        public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Looking up a proposal never runs a capability.");
        public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
            capability == new AssistantCapabilityReference(Id, 1)
                ? codec.ParseOutput(new(ObservedReadAssistantSource.ProposalId, 1), outputJson) : AiAssistantErrors.InvalidOutput;
    }

    private sealed class Reader(AiAssistantTurnRow row, IReadOnlyList<AiAssistantTurnRow> turns) : IAiAssistantReader
    {
        public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(ownerUserId == row.OwnerUserId && requestId == row.RequestId ? row : null);
        public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<AiAssistantTurnRow>(turns.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToArray(), request.Page, request.PageSize, turns.Count));
        public Task<IReadOnlyList<Guid>> ListRetainedAudioRequestIdsAsync(IReadOnlyCollection<Guid> audioRequestIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> ListRetainedAttachmentKeysAsync(IReadOnlyCollection<string> attachmentKeys, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
