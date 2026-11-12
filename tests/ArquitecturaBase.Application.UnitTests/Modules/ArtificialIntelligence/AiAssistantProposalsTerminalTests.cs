using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Casos del asistente: assistant_proposal@2 selects up to three observed proposals of the turn, in order. The first terminal keeps
// selecting one.
public sealed class AiAssistantProposalsTerminalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AssistantChannelContext Context(Guid actor, int terminal) => new(
        "owner-channel", actor, actor, "es", "UTC", new([], []), [new("fictional.proposal", 1), new("fictional.other", 1)],
        [new("assistant_proposal", terminal)]);

    private static AssistantObservedProposal Observed(Guid turn, string capability = "fictional.proposal") =>
        new(turn, Guid.NewGuid(), new(capability, 1), new FictionalAssistantProposalPayload(Guid.NewGuid(), "fictitious-private-value"));

    private static string Selection(params Guid[] ids) => JsonSerializer.Serialize(new { proposalIds = ids });

    private static LlmConversationResponse ProposalCall(ObservedReadAssistantScenario scenario, string callId) => new("tools",
        [new(callId, ObservedReadAssistantSource.ProposalId, JsonSerializer.Serialize(new { resourceId = scenario.Source.ResourceId }))], null, null);

    [Fact]
    public void The_second_terminal_selects_up_to_three_observed_proposals_of_the_turn_in_the_models_order()
    {
        var actor = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var first = Observed(turn);
        var second = Observed(turn, "fictional.other");
        var third = Observed(turn);
        var selected = AiAssistantTerminalPolicy.ValidateProposals(Context(actor, 2), turn, "assistant_proposal",
            Selection(third.Id, first.Id, second.Id), [first, second, third]);
        Assert.True(selected.IsSuccess);
        Assert.Equal(new[] { third.Id, first.Id, second.Id }, selected.Value.Select(proposal => proposal.Id));
        Assert.Same(third.Payload, selected.Value[0].Payload);
    }

    [Fact]
    public void A_channel_closing_with_the_first_terminal_cannot_select_several_and_the_first_rejects_the_second_shape()
    {
        var actor = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var proposal = Observed(turn);
        Assert.Equal(AiAssistantErrors.ToolRejected, AiAssistantTerminalPolicy.ValidateProposals(Context(actor, 1), turn, "assistant_proposal",
            Selection(proposal.Id), [proposal]).Error);
        Assert.Equal(AiAssistantErrors.ToolRejected, AiAssistantTerminalPolicy.ValidateProposal(Context(actor, 2), turn, "assistant_proposal",
            JsonSerializer.Serialize(new { proposalId = proposal.Id }), [proposal]).Error);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("four")]
    [InlineData("repeated")]
    [InlineData("unknown")]
    [InlineData("foreign")]
    [InlineData("extra")]
    [InlineData("not-a-list")]
    [InlineData("not-an-id")]
    public void Malformed_or_foreign_selections_are_rejected_before_any_owner_executes(string variant)
    {
        var actor = Guid.NewGuid();
        var turn = Guid.NewGuid();
        var proposals = Enumerable.Range(0, 4).Select(_ => Observed(turn)).ToList();
        var foreign = Observed(Guid.NewGuid());
        proposals.Add(foreign);
        var json = variant switch
        {
            "empty" => Selection(),
            "four" => Selection(proposals[0].Id, proposals[1].Id, proposals[2].Id, proposals[3].Id),
            "repeated" => Selection(proposals[0].Id, proposals[0].Id),
            "unknown" => Selection(proposals[0].Id, Guid.NewGuid()),
            "foreign" => Selection(foreign.Id),
            "extra" => JsonSerializer.Serialize(new { proposalIds = new[] { proposals[0].Id }, proposalId = proposals[0].Id }),
            "not-a-list" => JsonSerializer.Serialize(new { proposalIds = proposals[0].Id }),
            _ => """{"proposalIds":["not-a-guid"]}""",
        };
        var result = AiAssistantTerminalPolicy.ValidateProposals(Context(actor, 2), turn, "assistant_proposal", json, proposals);
        Assert.Equal(AiAssistantErrors.InvalidOutput, result.Error);
    }

    [Fact]
    public async Task Several_selected_proposals_are_completed_in_order_and_read_back_with_their_reads()
    {
        var scenario = new ObservedReadAssistantScenario(AiAssistantDispatchServiceTests.CreateObservedReadStore, preloadRead: true, proposalTerminal: 2);
        var context = scenario.Context();
        context = (await scenario.Execution.PreloadAsync(context with { ProviderRequest = context.ProviderRequest with { Messages = [new("user", "Fictional request")] } }, Ct)).Value;
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var first = await scenario.Execution.AdvanceAsync(context, ProposalCall(scenario, "first-proposal"), Ct);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, first.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var second = await scenario.Execution.AdvanceAsync(first.Value.Context, ProposalCall(scenario, "second-proposal"), Ct);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, second.Value, Ct)).IsSuccess);
        var proposals = second.Value.Context.Proposals;
        Assert.Equal(2, proposals.Count);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var selected = await scenario.Execution.AdvanceAsync(second.Value.Context, new("tools",
            [new("select-call", "assistant_proposal", Selection(proposals[1].Id, proposals[0].Id))], null, null), Ct);
        Assert.True(selected.IsSuccess);
        Assert.Equal(new[] { proposals[1].Id, proposals[0].Id }, selected.Value.Proposals.Select(proposal => proposal.Id));
        Assert.Equal(proposals[1].Id, selected.Value.Proposal!.Id);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, selected.Value, Ct)).IsSuccess);
        var row = scenario.Row();
        var answer = scenario.Protector.Unprotect(row.OwnerUserId, row.ConversationId, row.Id, "answer", row.PayloadRevision, row.ProtectedAnswer!);
        using (var stored = JsonDocument.Parse(answer!))
        {
            Assert.Equal("proposals", stored.RootElement.GetProperty("kind").GetString());
            Assert.Equal(new[] { 3, 2 }, stored.RootElement.GetProperty("ordinals").EnumerateArray().Select(value => value.GetInt32()));
        }
        var mapped = scenario.ReloadedPolicy(new ObservedReadAssistantSource()).Map(row, true, 1);
        Assert.True(mapped.IsSuccess);
        Assert.Equal(new[] { proposals[1].Id, proposals[0].Id }, mapped.Value.ObservedProposals.Select(proposal => proposal.Id));
        Assert.Equal(proposals[1].Id, mapped.Value.ObservedProposal!.Id);
        Assert.All(mapped.Value.ObservedProposals, proposal => Assert.Equal(1, Assert.Single(proposal.ObservedReads).Ordinal));
    }

    [Fact]
    public async Task One_proposal_selected_with_the_second_terminal_is_stored_like_the_first()
    {
        var scenario = new ObservedReadAssistantScenario(AiAssistantDispatchServiceTests.CreateObservedReadStore, preloadRead: true, proposalTerminal: 2);
        var context = scenario.Context();
        context = (await scenario.Execution.PreloadAsync(context with { ProviderRequest = context.ProviderRequest with { Messages = [new("user", "Fictional request")] } }, Ct)).Value;
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var proposed = await scenario.Execution.AdvanceAsync(context, ProposalCall(scenario, "only-proposal"), Ct);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, proposed.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var proposal = Assert.Single(proposed.Value.Context.Proposals);
        var selected = await scenario.Execution.AdvanceAsync(proposed.Value.Context, new("tools",
            [new("select-call", "assistant_proposal", Selection(proposal.Id))], null, null), Ct);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, selected.Value, Ct)).IsSuccess);
        var row = scenario.Row();
        var answer = scenario.Protector.Unprotect(row.OwnerUserId, row.ConversationId, row.Id, "answer", row.PayloadRevision, row.ProtectedAnswer!);
        using var stored = JsonDocument.Parse(answer!);
        Assert.Equal(new[] { "kind", "ordinal" }, stored.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        var mapped = scenario.ReloadedPolicy(new ObservedReadAssistantSource()).Map(row, true, 1);
        Assert.Equal(proposal.Id, Assert.Single(mapped.Value.ObservedProposals).Id);
    }

    [Fact]
    public async Task A_channel_can_be_accepted_with_the_second_terminal()
    {
        var fixture = new AssistantOperationsFixture();
        var request = fixture.Request() with { Context = fixture.Context with { AllowedTerminals = [new("assistant_proposal", 2)] } };
        var accepted = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(accepted.IsSuccess);
    }

    [Fact]
    public async Task Operations_return_every_selected_proposal_in_order_and_the_first_as_the_proposal()
    {
        var fixture = new AssistantOperationsFixture();
        var (turn, proposals) = fixture.SeedCompletedWithProposals(2);
        var result = await fixture.Service.GetAsync(fixture.Context, turn.RequestId, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(proposals.Select(proposal => proposal.Id), result.Value.Proposals.Select(proposal => proposal.Id));
        Assert.Equal(proposals[0].Id, result.Value.Proposal!.Id);
    }

    [Fact]
    public void The_history_carries_every_selected_proposal_of_the_latest_turn()
    {
        var turn = Guid.NewGuid();
        var first = Observed(turn);
        var second = Observed(turn, "fictional.other");
        var mapped = new AiAssistantTurnResponse(turn, Guid.NewGuid(), Guid.NewGuid(), 1, 1, 1, "completed", "completed", "allowed",
            "Fictional request", null, [], null, null, "es", "UTC", DateTime.UnixEpoch, DateTime.UnixEpoch, 1)
            { ObservedProposal = first, ObservedProposals = [first, second] };
        var messages = AiAssistantHistoryPolicy.Messages([mapped], 24000, "es");
        var answer = Assert.Single(messages, message => message.Role == "assistant").Text;
        Assert.Equal(2, answer.Split(AssistantTexts.Get("Prompt.PreviousProposal", "es").Split('{')[0], StringSplitOptions.None).Length - 1);
    }
}
