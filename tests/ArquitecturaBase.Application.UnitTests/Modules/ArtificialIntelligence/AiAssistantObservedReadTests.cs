using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantObservedReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ObservedReadAssistantScenario Scenario() => new(AiAssistantDispatchServiceTests.CreateObservedReadStore);
    private static LlmConversationResponse ReadCall() => new("tools", [new("read-call", ObservedReadAssistantSource.ReadId, "{}")], "fictional-read-continuation", null);
    private static LlmConversationResponse ProposalCall(ObservedReadAssistantScenario scenario) => new("tools",
        [new("proposal-call", ObservedReadAssistantSource.ProposalId, JsonSerializer.Serialize(new { resourceId = scenario.Source.ResourceId }))],
        "fictional-proposal-continuation", null);

    [Fact]
    public async Task Observed_metadata_is_required_before_a_guessed_resource_can_be_proposed()
    {
        var scenario = Scenario();
        var result = await scenario.Execution.AdvanceAsync(scenario.Context(), ProposalCall(scenario), Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
        Assert.Equal(0, scenario.Source.ReadExecutions);
        Assert.Equal(1, scenario.Source.ProposalExecutions);
        Assert.Empty(scenario.Source.LastProposalRequest!.ObservedReads);
        Assert.Empty(scenario.Store.Tools);
    }

    [Fact]
    public async Task Observed_reads_from_another_turn_are_rejected_before_the_proposal_source_executes()
    {
        var scenario = Scenario();
        var context = scenario.Context() with
        {
            ObservedReads = [new(Guid.NewGuid(), 1, new(ObservedReadAssistantSource.ReadId, 1),
                new ObservedResourceReadPayload(scenario.Source.ResourceId, 7, scenario.Source.CurrentName))]
        };
        var result = await scenario.Execution.AdvanceAsync(context, ProposalCall(scenario), Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
        Assert.Equal(0, scenario.Source.ProposalExecutions);
        Assert.Empty(scenario.Store.Tools);
    }

    [Fact]
    public async Task Observed_reads_created_in_the_same_provider_batch_cannot_authorize_its_proposal()
    {
        var scenario = Scenario();
        var response = new LlmConversationResponse("tools", [ReadCall().ToolCalls[0], ProposalCall(scenario).ToolCalls[0]], null, null);
        var result = await scenario.Execution.AdvanceAsync(scenario.Context(), response, Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
        Assert.Equal(1, scenario.Source.ReadExecutions);
        Assert.Equal(1, scenario.Source.ProposalExecutions);
        Assert.Empty(scenario.Source.LastProposalRequest!.ObservedReads);
        Assert.Empty(scenario.Store.Tools);
    }

    [Fact]
    public async Task Observed_reads_from_an_earlier_confirmed_step_pass_the_original_typed_snapshot()
    {
        var scenario = Scenario();
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadCall(), Ct);
        Assert.True(read.IsSuccess);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, read.Value, Ct)).IsSuccess);
        var original = Assert.Single(read.Value.Context.ObservedReads);
        var snapshot = Assert.IsType<ObservedResourceReadPayload>(original.Payload);
        var emitted = Assert.Single(read.Value.Context.ProviderRequest.ToolResults);
        Assert.Contains(snapshot.Name, emitted.ResultJson, StringComparison.Ordinal);
        scenario.Source.CurrentName = "new-name-after-observation";
        scenario.Source.CurrentRevision = 8;
        var proposed = await scenario.Execution.AdvanceAsync(read.Value.Context, ProposalCall(scenario), Ct);
        Assert.True(proposed.IsSuccess);
        Assert.Same(original, Assert.Single(scenario.Source.LastProposalRequest!.ObservedReads));
        var proposal = Assert.Single(proposed.Value.Context.Proposals);
        Assert.Same(original, Assert.Single(proposal.ObservedReads));
        Assert.Equal(new ObservedResourceProposalPayload(snapshot.ResourceId, snapshot.Revision, snapshot.Name),
            Assert.IsType<ObservedResourceProposalPayload>(proposal.Payload));
        Assert.Equal(1, scenario.Source.ReadExecutions);
        Assert.Equal(1, scenario.Source.ProposalExecutions);
    }

    [Fact]
    public async Task Observed_read_recovery_requires_the_exact_registered_codec_version()
    {
        var scenario = Scenario();
        await CompleteObservedFlowAsync(scenario);
        var row = scenario.Row();
        row = row with { ToolResults = row.ToolResults.Select(tool => tool.Ordinal == 1 ? tool with { Version = 2 } : tool).ToArray() };
        var reloaded = new ObservedReadAssistantSource();
        var result = scenario.ReloadedPolicy(reloaded).Map(row, true, 1);
        Assert.Equal(AiAssistantErrors.ContentUnavailable, result.Error);
        Assert.Equal(0, reloaded.ReadParseCalls);
        Assert.Equal(0, reloaded.ReadExecutions);
        Assert.Equal(0, reloaded.ProposalExecutions);
    }

    [Fact]
    public async Task Observed_read_references_roundtrip_through_real_progress_completion_and_a_fresh_codec()
    {
        var scenario = Scenario();
        var selected = await CompleteObservedFlowAsync(scenario);
        var row = scenario.Row();
        var answer = scenario.Protector.Unprotect(row.OwnerUserId, row.ConversationId, row.Id, "answer", row.PayloadRevision, row.ProtectedAnswer!);
        using (var completion = JsonDocument.Parse(answer!))
        {
            Assert.Equal(new[] { "kind", "ordinal" }, completion.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
            Assert.Equal("proposal", completion.RootElement.GetProperty("kind").GetString());
            Assert.Equal(2, completion.RootElement.GetProperty("ordinal").GetInt32());
        }
        var storedProposal = Assert.Single(row.ToolResults, tool => tool.Ordinal == 2);
        var stored = scenario.Protector.Unprotect(row.OwnerUserId, row.ConversationId, row.Id, "tool:2", row.PayloadRevision, storedProposal.ProtectedResult!);
        using (var envelope = JsonDocument.Parse(stored!))
            Assert.Equal(new[] { 1 }, envelope.RootElement.GetProperty("readOrdinals").EnumerateArray().Select(value => value.GetInt32()));
        var codec = new ObservedReadAssistantSource { CurrentName = "different-current-metadata", CurrentRevision = 99 };
        var result = scenario.ReloadedPolicy(codec).Map(row, true, 1);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Proposal);
        Assert.Equal(selected.Id, result.Value.Proposal.Id);
        Assert.Equal(selected.Capability, result.Value.Proposal.Capability);
        Assert.Equal(Assert.IsType<ObservedResourceProposalPayload>(selected.Payload), Assert.IsType<ObservedResourceProposalPayload>(result.Value.Proposal.Payload));
        var read = Assert.Single(result.Value.Proposal.ObservedReads);
        Assert.Equal(row.Id, read.TurnId);
        Assert.Equal(1, read.Ordinal);
        Assert.Equal(new AssistantCapabilityReference(ObservedReadAssistantSource.ReadId, 1), read.Capability);
        Assert.Equal(Assert.IsType<ObservedResourceReadPayload>(Assert.Single(selected.ObservedReads).Payload), Assert.IsType<ObservedResourceReadPayload>(read.Payload));
        Assert.Equal(1, codec.ReadParseCalls);
        Assert.Equal(1, codec.ProposalParseCalls);
        Assert.Equal(0, codec.ReadExecutions);
        Assert.Equal(0, codec.ProposalExecutions);
        Assert.Equal(1, scenario.Source.ReadExecutions);
        Assert.Equal(2, scenario.Store.Tools.Count);
    }

    [Fact]
    public async Task Observed_read_recovery_fails_before_returning_a_proposal_when_its_protected_read_is_missing()
    {
        var scenario = Scenario();
        await CompleteObservedFlowAsync(scenario);
        var row = scenario.Row();
        row = row with { ToolResults = row.ToolResults.Select(tool => tool.Ordinal == 1 ? tool with { ProtectedResult = null } : tool).ToArray() };
        var codec = new ObservedReadAssistantSource();
        var result = scenario.ReloadedPolicy(codec).Map(row, true, 1);
        Assert.Equal(AiAssistantErrors.ContentUnavailable, result.Error);
        Assert.Equal(0, codec.ReadExecutions);
        Assert.Equal(0, codec.ProposalExecutions);
    }

    [Fact]
    public async Task Observed_proposals_and_their_read_snapshots_are_omitted_from_the_public_turn_DTO()
    {
        var scenario = Scenario();
        await CompleteObservedFlowAsync(scenario);
        var mapped = scenario.ReloadedPolicy(new ObservedReadAssistantSource()).Map(scenario.Row(), true, 1);
        Assert.True(mapped.IsSuccess);
        Assert.NotNull(mapped.Value.Proposal);
        var json = JsonSerializer.Serialize(mapped.Value, AiAssistantResponsePolicy.JsonOptions);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("proposal", out _));
        Assert.DoesNotContain("observedReads", json, StringComparison.Ordinal);
        Assert.DoesNotContain(scenario.Source.CurrentName, json, StringComparison.Ordinal);
    }

    private static async Task<AssistantObservedProposal> CompleteObservedFlowAsync(ObservedReadAssistantScenario scenario)
    {
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadCall(), Ct);
        Assert.True(read.IsSuccess);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, read.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var proposal = await scenario.Execution.AdvanceAsync(read.Value.Context, ProposalCall(scenario), Ct);
        Assert.True(proposal.IsSuccess);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, proposal.Value, Ct)).IsSuccess);
        var observed = Assert.Single(proposal.Value.Context.Proposals);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var selected = await scenario.Execution.AdvanceAsync(proposal.Value.Context,
            new("tools", [new("select-call", "assistant_proposal", JsonSerializer.Serialize(new { proposalId = observed.Id }))], null, null), Ct);
        Assert.True(selected.IsSuccess);
        Assert.NotNull(selected.Value.Proposal);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, selected.Value, Ct)).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Completed, scenario.Store.Turn.State);
        return selected.Value.Proposal;
    }
}

public sealed partial class AiAssistantDispatchServiceTests
{
    internal static ObservedReadAssistantStorePorts CreateObservedReadStore(Guid actorId, DateTime nowUtc, string channelId)
    {
        var store = new Store(actorId, nowUtc, channelId);
        return new(store, store, store, store.Conversation, store.Turn, store.Tools);
    }
}
