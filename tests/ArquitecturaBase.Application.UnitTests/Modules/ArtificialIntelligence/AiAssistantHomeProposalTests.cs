using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantHomeProposalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ObservedReadAssistantScenario Scenario() => new(AiAssistantDispatchServiceTests.CreateObservedReadStore, home: true);
    private static LlmConversationResponse ReadCall() => new("tools", [new("read-call", ObservedReadAssistantSource.ReadId, "{}")], "fictional-read-continuation", null);
    private static LlmConversationResponse ProposalCall(ObservedReadAssistantScenario scenario) => new("tools",
        [new("proposal-call", ObservedReadAssistantSource.ProposalId, JsonSerializer.Serialize(new { resourceId = scenario.Source.ResourceId }))],
        "fictional-proposal-continuation", null);
    private static LlmConversationResponse Answer(string kind, string text, Guid? proposalId) => new("tools",
        [new("answer-call", "assistant_answer", JsonSerializer.Serialize(new { kind, text, evidenceIds = Array.Empty<Guid>(), proposalId }))], null, null);

    private static async Task<AiAssistantAdvanceResponse> ProposeAsync(ObservedReadAssistantScenario scenario)
    {
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadCall(), Ct);
        Assert.True(read.IsSuccess);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, read.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var proposal = await scenario.Execution.AdvanceAsync(read.Value.Context, ProposalCall(scenario), Ct);
        Assert.True(proposal.IsSuccess);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, proposal.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        return proposal.Value;
    }

    [Fact]
    public async Task Home_closes_on_its_observed_proposal_with_the_explanation_and_shows_the_owner_content()
    {
        var scenario = Scenario();
        var proposal = await ProposeAsync(scenario);
        var observed = Assert.Single(proposal.Context.Proposals);
        var closed = await scenario.Execution.AdvanceAsync(proposal.Context, Answer("proposal", "Armé la propuesta con lo que pediste.", observed.Id), Ct);
        Assert.True(closed.IsSuccess);
        Assert.Equal(observed.Id, closed.Value.Proposal!.Id);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, closed.Value, Ct)).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Completed, scenario.Store.Turn.State);
        var mapped = scenario.ReloadedPolicy(new ObservedReadAssistantSource()).Map(scenario.Row(), true, 1);
        Assert.True(mapped.IsSuccess);
        Assert.Equal(("proposal", "Armé la propuesta con lo que pediste."), (mapped.Value.Answer!.Kind, mapped.Value.Answer.Text));
        Assert.Empty(mapped.Value.Answer.EvidenceIds);
        Assert.Equal(observed.Id, mapped.Value.ObservedProposal!.Id);
        Assert.Equal((observed.Id, ObservedReadAssistantSource.ProposalId, 1), (mapped.Value.Proposal!.Id, mapped.Value.Proposal.Capability, mapped.Value.Proposal.Version));
        Assert.Equal(JsonValueKind.Object, mapped.Value.Proposal.Content.ValueKind);
    }

    [Fact]
    public async Task Home_cannot_close_on_a_proposal_this_turn_did_not_observe()
    {
        var scenario = Scenario();
        var proposal = await ProposeAsync(scenario);
        var result = await scenario.Execution.AdvanceAsync(proposal.Context, Answer("proposal", "Listo.", Guid.NewGuid()), Ct);
        Assert.Equal(AiAssistantErrors.InvalidOutput, result.Error);
    }

    [Theory]
    [InlineData("proposal", false)]
    [InlineData("clarification", true)]
    public async Task Only_a_proposal_answer_names_a_proposal(string kind, bool withProposal)
    {
        var scenario = Scenario();
        var proposal = await ProposeAsync(scenario);
        var observed = Assert.Single(proposal.Context.Proposals);
        var result = await scenario.Execution.AdvanceAsync(proposal.Context, Answer(kind, "Contame qué querés cambiar.", withProposal ? observed.Id : null), Ct);
        Assert.Equal(AiAssistantErrors.InvalidOutput, result.Error);
    }

    [Fact]
    public async Task Home_still_answers_without_a_proposal()
    {
        var scenario = Scenario();
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var result = await scenario.Execution.AdvanceAsync(scenario.Context(), Answer("clarification", "¿Qué querés armar?", null), Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Proposal);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, result.Value, Ct)).IsSuccess);
        var mapped = scenario.ReloadedPolicy(new ObservedReadAssistantSource()).Map(scenario.Row(), true, 1);
        Assert.Equal("clarification", mapped.Value.Answer!.Kind);
        Assert.Null(mapped.Value.Proposal);
    }

    [Fact]
    public async Task An_uninstalled_owner_leaves_the_explanation_readable_without_its_card()
    {
        var scenario = Scenario();
        var proposal = await ProposeAsync(scenario);
        var observed = Assert.Single(proposal.Context.Proposals);
        var closed = await scenario.Execution.AdvanceAsync(proposal.Context, Answer("proposal", "Armé la propuesta.", observed.Id), Ct);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, closed.Value, Ct)).IsSuccess);
        var withoutOwner = new ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantResponsePolicy(scenario.Protector, scenario.Clock,
            new ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantCapabilityCatalogPolicy([], scenario.Permissions));
        var mapped = withoutOwner.Map(scenario.Row(), true, 1);
        Assert.True(mapped.IsSuccess);
        Assert.Equal("Armé la propuesta.", mapped.Value.Answer!.Text);
        Assert.Null(mapped.Value.Proposal);
        Assert.Null(mapped.Value.ObservedProposal);
    }
}
