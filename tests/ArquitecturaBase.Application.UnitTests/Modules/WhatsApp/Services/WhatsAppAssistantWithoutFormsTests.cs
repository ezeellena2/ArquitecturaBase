using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// B5: without Formularios the engine rejects a context that names a capability nobody declares, so WhatsApp asks for the `forms.*` ones
/// only while an installed owner declares them: the automatic conversation, with and without an agent, Atención manual and Probar. The
/// composition with the real engine is <c>WhatsAppAssistantWithoutFormsCompositionTests</c>.
/// </summary>
public sealed class WhatsAppAssistantWithoutFormsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("automatic", false)]
    [InlineData("automatic", true)]
    [InlineData("manual", false)]
    [InlineData("manual", true)]
    [InlineData("test", true)]
    public async Task Without_forms_no_context_asks_for_a_forms_capability_and_with_them_it_still_does(string kind, bool agent)
    {
        var without = await ContextAsync(new WhatsAppAssistantContextFixture(withForms: false), kind, agent);
        var with = await ContextAsync(new WhatsAppAssistantContextFixture(), kind, agent);

        Assert.DoesNotContain(Requested(without), reference => reference.Id.StartsWith("forms.", StringComparison.Ordinal));
        Assert.NotEmpty(without.AllowedCapabilities);
        Assert.Contains(Requested(with), reference => reference.Id.StartsWith("forms.", StringComparison.Ordinal));
        // Only what Formularios declares goes away: WhatsApp's own capabilities and terminals stay the same.
        Assert.Equal(Requested(with).Where(reference => !reference.Id.StartsWith("forms.", StringComparison.Ordinal)), Requested(without));
        Assert.Equal(with.AllowedTerminals, without.AllowedTerminals);
    }

    private static IEnumerable<AssistantCapabilityReference> Requested(AssistantChannelContext context) =>
        context.AllowedCapabilities.Concat(context.PreloadedCapabilities ?? []);

    private static async Task<AssistantChannelContext> ContextAsync(WhatsAppAssistantContextFixture fixture, string kind, bool agent)
    {
        if (agent) fixture.Agents.Agent = Agent();
        switch (kind)
        {
            case "automatic":
                fixture.Contact.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);
                return (await fixture.Policy.CaptureAsync(fixture.Contact, null, Ct)).Value.Context;
            case "manual":
                return (await fixture.Policy.CaptureAsync(fixture.Contact, fixture.Operator.Id, Ct)).Value.Context;
            default:
                var test = new WhatsAppAssistantAgentTestPolicy(fixture.Recipients, fixture.Settings, new FakeAssistantForms(),
                    new WhatsAppAssistantPresentationFixture.RecordSource(), fixture.Capabilities);
                return (await test.ContextAsync(fixture.Operator.Id, fixture.Contact.Id, Ct)).Value;
        }
    }

    private static WhatsAppAssistantAgentRow Agent() => new(1, "es",
        new("Asistente", "Soy el asistente del negocio.", "Cercano.", "Responde consultas.", "No da precios.", new("Te paso con una persona del equipo.", null)),
        [new(Guid.Parse("018f0000-0000-7000-8000-000000000302"), "Pregunta por el negocio", WhatsAppAssistantInputType.Any, true,
            new(WhatsAppAssistantActionKind.DraftedReply, Instructions: "Respondé con la información del negocio."))],
        new(WhatsAppAssistantActionKind.NoReply), WhatsAppAssistantAgentLimits.DefaultLimits);
}
