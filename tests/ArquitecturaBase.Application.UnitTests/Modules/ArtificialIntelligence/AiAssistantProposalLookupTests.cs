using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
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

    private static async Task<(ObservedReadAssistantScenario Scenario, Guid ProposalId)> CompletedAsync(bool home = true, bool ownConversation = false)
    {
        var scenario = new ObservedReadAssistantScenario(AiAssistantDispatchServiceTests.CreateObservedReadStore, home, ownConversation);
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

    // Whether a later proposal replaced it never asks for a sealed spreadsheet, and a turn without one has no descriptor to ask for.
    [Fact]
    public async Task Deciding_whether_it_is_current_never_reads_the_spreadsheets_of_later_turns()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var row = scenario.Row();
        var later = row with { Id = Guid.NewGuid(), Ordinal = row.Ordinal + 1, AttachmentKey = "fixture/attachments/clientes.xlsx" };
        var reader = new Reader(row, [later, row]);
        var result = await Lookup(scenario, reader).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.False(result.Value.IsCurrent);
        Assert.Null(result.Value.Attachment);
        Assert.Empty(reader.SealedReads);
    }

    // Stage 2: the proposal's turn names its spreadsheet, its own or inherited, for the owner to open the copy; only that turn's sealed
    // descriptor is read, and only because the turn has one.
    [Fact]
    public async Task The_spreadsheet_of_the_proposals_turn_comes_with_it()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var key = "ai/assistant/attachments/" + Guid.NewGuid().ToString("N") + ".xlsx";
        var row = scenario.Row() with { AttachmentKey = key, AttachmentSha256 = Sha256, AttachmentSize = 2048 };
        var later = row with { Id = Guid.NewGuid(), Ordinal = row.Ordinal + 1, ToolResults = [], AttachmentKey = key };
        var reader = new Reader(row, [later, row]);
        reader.Sealed[row.Id] = Seal(scenario, row, key);
        var result = await Lookup(scenario, reader).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.Equal(new AssistantAttachmentDescriptor(key, "clientes.xlsx", Xlsx, 2048, Sha256), result.Value.Attachment);
        Assert.True(result.Value.IsCurrent);
        Assert.Equal([row.Id], Assert.Single(reader.SealedReads));
    }

    [Fact]
    public async Task A_turn_whose_sealed_spreadsheet_cannot_be_read_is_unavailable()
    {
        var (scenario, proposalId) = await CompletedAsync();
        var row = scenario.Row() with { AttachmentKey = "ai/assistant/attachments/" + Guid.NewGuid().ToString("N") + ".csv", AttachmentSha256 = Sha256, AttachmentSize = 2048 };
        var result = await Lookup(scenario, new Reader(row, [row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.Equal(AiAssistantErrors.ContentUnavailable, result.Error);
    }

    // The actor's own conversation in a channel: the assistant mode with the actor as its Subject. Its provenance is the original
    // instant its seal keeps, and a later proposal of the group in the same engine conversation replaces it, as in Home.
    [Fact]
    public async Task A_proposal_of_the_actors_own_channel_conversation_is_found_with_its_original_instant()
    {
        var (scenario, proposalId) = await CompletedAsync(home: false, ownConversation: true);
        var row = scenario.Row();
        var result = await Lookup(scenario, new Reader(row, [row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal((row.ChannelId, scenario.Profile.OccurredAtUtc!.Value), (result.Value.Provenance.ChannelId, result.Value.Provenance.OccurredAtUtc));
        Assert.Equal(("capabilities-v1", 1), (result.Value.Provenance.InstructionsId, result.Value.Provenance.InstructionsVersion));
        Assert.Equal((AssistantInteractionMode.Assistant, (Guid?)row.OwnerUserId), (result.Value.Context.InteractionMode, result.Value.Context.SubjectUserId));
        Assert.True(result.Value.IsCurrent);
        var later = row with { Id = Guid.NewGuid(), Ordinal = row.Ordinal + 1 };
        Assert.False((await Lookup(scenario, new Reader(row, [later, row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct)).Value.IsCurrent);
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("another-subject")]
    [InlineData("without-instant")]
    public async Task Another_channel_conversation_is_not_the_actors_own(string kind)
    {
        var (scenario, proposalId) = await CompletedAsync(home: false, ownConversation: true);
        var context = scenario.Profile.ChannelContext;
        var profile = kind switch
        {
            "manual" => scenario.Profile with { ChannelContext = context with { InteractionMode = AssistantInteractionMode.Manual } },
            "another-subject" => scenario.Profile with { ChannelContext = context with { SubjectUserId = Guid.NewGuid() } },
            _ => scenario.Profile with { OccurredAtUtc = null },
        };
        var row = scenario.Row() with { ExecutionProfileJson = JsonSerializer.Serialize(profile, AiAssistantResponsePolicy.JsonOptions) };
        var result = await Lookup(scenario, new Reader(row, [row])).FindAsync(row.OwnerUserId, row.RequestId, proposalId, Group, Ct);
        Assert.Equal(kind == "without-instant" ? AiAssistantErrors.ContentUnavailable : AiAssistantErrors.NotFound, result.Error);
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

    // A channel turn that is not the actor's own conversation (here without a Subject or a mode) keeps its own "latest" rule.
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

    private const string Sha256 = "8f434346648f6b96df89dda901c5176b10a6d83961dd3c1ac88b59b2dc327aa4";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // The descriptor a turn sealed with the prepared sheet of the same file, as the submission seals it.
    private static string Seal(ObservedReadAssistantScenario scenario, AiAssistantTurnRow row, string key)
    {
        var sheet = new StructuredSpreadsheet("clientes.xlsx", StructuredSpreadsheetKind.Xlsx, Xlsx, 2048, Sha256, "Hoja1", 3, 2,
            [new(1, new Dictionary<string, string> { ["A"] = "CUIT", ["B"] = "Razón social" })]);
        var attachment = new AssistantAttachmentInput(key, "clientes.xlsx", Xlsx, 2048, Sha256, row.ContentExpiresAtUtc, sheet);
        return scenario.Protector.Protect(row.OwnerUserId, row.ConversationId, row.Id, AiAssistantAttachmentPolicy.Purpose, row.PayloadRevision,
            AiAssistantAttachmentPolicy.Serialize(attachment));
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
        public Dictionary<Guid, string> Sealed { get; } = [];
        public List<Guid[]> SealedReads { get; } = [];
        public Task<IReadOnlyDictionary<Guid, string>> ListProtectedAttachmentsAsync(Guid ownerUserId, IReadOnlyCollection<Guid> turnIds, CancellationToken cancellationToken)
        {
            SealedReads.Add([.. turnIds]);
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(ownerUserId == row.OwnerUserId
                ? Sealed.Where(pair => turnIds.Contains(pair.Key)).ToDictionary() : new Dictionary<Guid, string>());
        }
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
