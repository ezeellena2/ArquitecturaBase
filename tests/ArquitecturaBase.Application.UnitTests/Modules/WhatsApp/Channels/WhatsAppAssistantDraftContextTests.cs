using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

/// <summary>
/// ADR 0021, point 8, WhatsApp's half: with a saved agent, the automatic conversation, Atención manual and Probar ask for the drafted
/// reply, and it is the only capability there that drafts text. The engine's half, that it leaves such a capability out unless the model
/// profile is qualified to draft, is <c>AiAssistantDraftQualificationTests</c>; each module tests its own side of the boundary.
/// </summary>
public sealed class WhatsAppAssistantDraftContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly AssistantCapabilityReference DraftReply = new(WhatsAppAssistantAgentCapabilitySource.DraftId, 1);

    [Theory]
    [InlineData("automatic")]
    [InlineData("manual")]
    [InlineData("test")]
    public async Task Every_context_with_an_agent_asks_for_the_drafted_reply_as_its_only_capability_that_drafts_text(string kind)
    {
        var fixture = new WhatsAppAssistantContextFixture();
        fixture.Agents.Agent = Agent();
        var definitions = new WhatsAppAssistantAgentCapabilitySource(fixture.Agents, new UnavailableBusinessKnowledgeSource(),
            new FakeWhatsAppAssistantHistoryReader(), new FakeWhatsAppAssistantReader(), fixture.Clock).Definitions;
        bool Drafts(AssistantCapabilityReference reference) =>
            definitions.Any(definition => definition.Id == reference.Id && definition.Version == reference.Version && definition.DraftsText);

        var context = await ContextAsync(fixture, kind);

        Assert.Equal([DraftReply], context.AllowedCapabilities.Where(Drafts));
        // A preload is never a tool, so nothing that drafts text is preloaded.
        Assert.DoesNotContain(context.PreloadedCapabilities ?? [], Drafts);
    }

    private static async Task<AssistantChannelContext> ContextAsync(WhatsAppAssistantContextFixture fixture, string kind)
    {
        switch (kind)
        {
            case "automatic":
                fixture.Contact.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);
                return (await fixture.Policy.CaptureAsync(fixture.Contact, null, Ct)).Value.Context;
            case "manual":
                return (await fixture.Policy.CaptureAsync(fixture.Contact, fixture.Operator.Id, Ct)).Value.Context;
            default:
                var test = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms(),
                    new WhatsAppAssistantPresentationFixture.RecordSource());
                return (await test.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;
        }
    }

    private static WhatsAppAssistantAgentRow Agent() => new(1, "es",
        new("Asistente", "Soy el asistente del negocio.", "Cercano.", "Responde consultas.", "No da precios.", new("Te paso con una persona del equipo.", null)),
        [new(Guid.Parse("018f0000-0000-7000-8000-000000000301"), "Pregunta por el negocio", WhatsAppAssistantInputType.Any, true,
            new(WhatsAppAssistantActionKind.DraftedReply, Instructions: "Respondé con la información del negocio."))],
        new(WhatsAppAssistantActionKind.NoReply), WhatsAppAssistantAgentLimits.DefaultLimits);
}
