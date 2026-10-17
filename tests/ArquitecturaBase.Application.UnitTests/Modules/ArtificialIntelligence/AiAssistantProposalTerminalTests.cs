using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantProposalTerminalTests
{
    private static AssistantChannelContext Context(Guid actor, bool allowProposal) => new(
        "owner-channel", actor, actor, "es", "UTC", new([], []), [new("fictional.proposal", 1)],
        [new(allowProposal ? "assistant_proposal" : "assistant_answer", 1)]);

    [Fact]
    public void A_terminal_selects_only_a_typed_proposal_from_the_same_turn_and_offered_capability()
    {
        var actor = Guid.NewGuid(); var turn = Guid.NewGuid();
        var proposal = new AssistantObservedProposal(turn, Guid.NewGuid(), new("fictional.proposal", 1),
            new FictionalAssistantProposalPayload(Guid.NewGuid(), "fictitious-private-value"));
        var json = "{\"proposalId\":\"" + proposal.Id + "\"}";
        var selected = AiAssistantTerminalPolicy.ValidateProposal(Context(actor, true), turn, "assistant_proposal", json, [proposal]);
        Assert.True(selected.IsSuccess);
        Assert.Same(proposal.Payload, selected.Value.Payload);
        Assert.Equal(proposal.Id, selected.Value.Id);
        Assert.DoesNotContain("fictitious-private-value", proposal.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fictitious-private-value", proposal.Payload.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_channel_cannot_use_an_unoffered_proposal_terminal()
    {
        var result = AiAssistantTerminalPolicy.ValidateProposal(Context(Guid.NewGuid(), false), Guid.NewGuid(),
            "assistant_proposal", "{}", []);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
    }

    [Theory]
    [InlineData("foreign") ]
    [InlineData("wrong-version") ]
    [InlineData("extra-values") ]
    [InlineData("duplicate-property") ]
    public void Foreign_or_rewritten_proposals_are_rejected_before_the_owner_can_execute(string variant)
    {
        var actor = Guid.NewGuid(); var turn = Guid.NewGuid();
        var observed = new AssistantObservedProposal(variant == "foreign" ? Guid.NewGuid() : turn, Guid.NewGuid(),
            new("fictional.proposal", variant == "wrong-version" ? 2 : 1), new FictionalAssistantProposalPayload(Guid.NewGuid(), "private"));
        var extra = variant == "extra-values" ? ",\"values\":{\"value\":\"replacement\"}"
            : variant == "duplicate-property" ? ",\"proposalId\":\"" + observed.Id + "\"" : "";
        var json = "{\"proposalId\":\"" + observed.Id + "\"" + extra + "}";
        var result = AiAssistantTerminalPolicy.ValidateProposal(Context(actor, true), turn, "assistant_proposal", json, [observed]);
        Assert.Equal(AiAssistantErrors.InvalidOutput, result.Error);
    }
}
