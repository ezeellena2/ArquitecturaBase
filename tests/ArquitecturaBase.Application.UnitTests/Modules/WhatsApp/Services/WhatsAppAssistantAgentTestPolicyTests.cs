using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Probar (AS3) works like a conversation with the agent: the same preloads, up to three proposals and the same checks.
public sealed class WhatsAppAssistantAgentTestPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Hours = Guid.Parse("018f0000-0000-7000-8000-000000000201");

    private static WhatsAppAssistantAgentRow Agent() => new(1, "es",
        new("Asistente", "Soy el asistente del negocio.", "Cercano.", "Responde consultas.", "No da precios.", new("Te paso con una persona del equipo.", null)),
        [new(Hours, "Pregunta por el horario", WhatsAppAssistantInputType.Any, true,
            new(WhatsAppAssistantActionKind.ExactText, Text: new("Abrimos de 9 a 18.", null)))],
        new(WhatsAppAssistantActionKind.NoReply), WhatsAppAssistantAgentLimits.DefaultLimits);

    private static AssistantOperationResponse Operation(params (string Capability, AssistantCapabilityPayload Payload)[] selected)
    {
        var turn = Guid.NewGuid();
        AssistantObservedProposal[] proposals = [.. selected.Select(item => new AssistantObservedProposal(turn, Guid.NewGuid(), new(item.Capability, 1), item.Payload))];
        return new(Guid.NewGuid(), "completed", proposals[0], null, null) { Proposals = proposals };
    }

    private static readonly WhatsAppAssistantReplyPayload ExactHours = new("exactText", "own", Hours, "Pregunta por el horario", "Pregunta el horario.",
        "Un texto anterior", null, null, null);

    [Fact]
    public async Task A_test_preloads_the_agent_like_a_conversation_and_closes_on_up_to_three_proposals()
    {
        var fixture = new WhatsAppAssistantContextFixture();
        var policy = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms());
        var context = (await policy.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;
        Assert.Equal(new AssistantCapabilityReference[] { new(WhatsAppAssistantAgentCapabilitySource.AgentId, 1), new("forms.assistant-candidates", 1),
            new(WhatsAppAssistantAgentCapabilitySource.HistoryId, 1) }, context.PreloadedCapabilities);
        Assert.DoesNotContain(context.AllowedCapabilities, capability => context.PreloadedCapabilities!.Contains(capability));
        Assert.Equal(new[] { new AssistantTerminalReference("assistant_proposal", 2) }, context.AllowedTerminals);
    }

    [Fact]
    public async Task Several_actions_are_shown_in_order_and_a_form_mixed_with_them_shows_the_handoff()
    {
        var fixture = new WhatsAppAssistantContextFixture();
        var policy = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms());
        var context = (await policy.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;

        var replies = await policy.ActionsAsync(Operation((WhatsAppAssistantAgentCapabilitySource.ReplyId, ExactHours),
            (WhatsAppAssistantAgentCapabilitySource.ReplyId, ExactHours)), Agent(), context, Ct);
        Assert.Equal(new[] { WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantActionKind.ExactText }, replies.Select(action => action.Kind));

        var mixed = await policy.ActionsAsync(Operation((WhatsAppAssistantAgentCapabilitySource.ReplyId, ExactHours),
            ("forms.propose-registration", new WhatsAppAssistantNoReplyPayload("not_form"))), Agent(), context, Ct);
        var handoff = Assert.Single(mixed);
        Assert.Equal((WhatsAppAssistantActionKind.Handoff, "replyNotAllowed", "Te paso con una persona del equipo."),
            (handoff.Kind, handoff.HandoffReasonCode, handoff.Text));
    }
}
