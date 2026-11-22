using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppAssistantAutomaticTests
{
    // O36 (2026-11-21): over the day's offers the form does not go out and a person answers, with the reason, instead of silence.
    [Fact]
    public async Task A_form_over_the_daily_offer_limit_hands_off_with_its_reason_instead_of_a_third_offer()
    {
        var host = await HostAsync(false); host.Reader.OfferCount = 2;
        host.Reader.Source = host.Reader.Source! with { Id = Guid.NewGuid(), Body = "También quiero registrar otra visita." };
        var service = Service(host);
        var result = await service.PrepareAsync(host.Context.Contact.Id, Ct);
        Assert.True(result.IsSuccess); Assert.NotNull(result.Value); Assert.False(result.Value.RecoverOnly);
        Assert.Equal(host.Reader.Source.Body, result.Value.Operation!.Text);
        var outcome = host.Assistant.Outcome.Value with { RequestId = result.Value.RequestId };
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id, result.Value.RequestId, outcome, Ct)).IsSuccess);
        var row = Assert.Single(host.Requests.Rows);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, row.State);
        Assert.Empty(host.Sessions.Rows);
        var contact = host.Context.Contact;
        Assert.Equal((WhatsAppConversationMode.Manual, true, "dailyOfferLimit"), (contact.Mode, contact.NeedsPerson, contact.HandoffReasonCode));
        // Without an agent there is no handoff message: the reference keeps why for the chat.
        Assert.Empty(host.Conversation.Outgoing);
        var why = WhatsAppAssistantReplyRecordCodec.Parse(row.OutcomeJson)!;
        Assert.Equal((WhatsAppAssistantActionKind.Handoff, "dailyOfferLimit"), (why.Kind, why.HandoffReasonCode));
        Assert.Equal(0, host.Assistant.Submits);
        Assert.Null((await service.PrepareAsync(host.Context.Contact.Id, Ct)).Value);
        Assert.Single(host.Requests.Rows);
    }

    [Fact]
    public async Task Over_the_saved_agent_offer_limit_the_handoff_message_goes_out_with_why()
    {
        var host = await HostAsync(false); host.Reader.OfferCount = 3;
        host.Agents.Agent = Agent(dailyOffers: 3);
        host.Reader.Source = host.Reader.Source! with { Id = Guid.NewGuid(), Body = "Quiero registrar otra visita." };
        var service = Service(host);
        // With an agent the turn waits for the burst to end.
        host.Context.Clock.Advance(WhatsAppAssistantReplyPolicy.BurstQuiet);
        var result = await service.PrepareAsync(host.Context.Contact.Id, Ct);
        var outcome = host.Assistant.Outcome.Value with { RequestId = result.Value!.RequestId };
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id, result.Value.RequestId, outcome, Ct)).IsSuccess);
        Assert.Empty(host.Sessions.Rows);
        Assert.Equal("dailyOfferLimit", host.Context.Contact.HandoffReasonCode);
        var handoff = Assert.Single(host.Conversation.Outgoing);
        Assert.Equal("Te paso con una persona del equipo.", handoff.Body);
        Assert.Equal("dailyOfferLimit", WhatsAppAssistantReplyRecordCodec.Parse(handoff.AssistantReplyJson)!.HandoffReasonCode);
    }

    // TO1-A (2026-11-21): the offers of a day come from the saved agent's limits; without one, two, as before.
    [Fact]
    public async Task The_saved_agent_sets_how_many_forms_it_offers_a_day()
    {
        var host = await HostAsync();
        var context = WhatsAppAssistantSuggestionPolicy.Context(host.Reader.Reference!);
        var automatic = WhatsAppAssistantAcceptanceFixture.CreateAutomatic(host);
        host.Reader.OfferCount = 2;
        Assert.False(await automatic.HasOfferCapacityAsync(host.Context.Contact.Id, context, Ct));
        host.Agents.Agent = Agent(dailyOffers: 3);
        Assert.True(await automatic.HasOfferCapacityAsync(host.Context.Contact.Id, context, Ct));
        host.Reader.OfferCount = 3;
        Assert.False(await automatic.HasOfferCapacityAsync(host.Context.Contact.Id, context, Ct));
        host.Agents.Agent = Agent(dailyOffers: 1);
        host.Reader.OfferCount = 0;
        Assert.True(await automatic.HasOfferCapacityAsync(host.Context.Contact.Id, context, Ct));
        host.Reader.OfferCount = 1;
        Assert.False(await automatic.HasOfferCapacityAsync(host.Context.Contact.Id, context, Ct));
    }

    private static WhatsAppAssistantAgentRow Agent(int dailyOffers) => new(1, "es",
        new WhatsAppAssistantProfile("Asistente", "", "", "", "", new("Te paso con una persona del equipo.", null)), [],
        new WhatsAppAssistantAction(WhatsAppAssistantActionKind.NoReply), WhatsAppAssistantAgentLimits.DefaultLimits with { DailyOfferLimit = dailyOffers });
    [Fact]
    public async Task Exhausted_quota_also_blocks_a_new_interpreted_followup_after_capture()
    {
        var fixture = await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        var candidate = fixture.Host.Forms.Suggestions.Value.Candidates[0] with { Mode = "Assistant", When = "Another visit is needed" };
        fixture.Host.Forms.Suggestions = fixture.Host.Forms.Suggestions.Value with { Candidates = [candidate] };
        fixture.Host.Reader.OfferCount = 2;
        var result = await fixture.Service.PrepareAsync(fixture.Host.Context.Contact.Id, Ct);
        Assert.True(result.IsSuccess); Assert.Null(result.Value);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, Assert.Single(fixture.Host.Requests.Rows).State);
        Assert.Equal(0, fixture.Host.Assistant.Submits);
    }
    [Fact]
    public async Task An_unaccepted_reference_rejected_during_recovery_is_durably_blocked()
    {
        var fixture = await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        var candidate = fixture.Host.Forms.Suggestions.Value.Candidates[0] with { Mode = "Assistant", When = "Another visit is needed" };
        fixture.Host.Forms.Suggestions = fixture.Host.Forms.Suggestions.Value with { Candidates = [candidate] };
        var host = fixture.Host; var service = fixture.Service;
        var first = await service.PrepareAsync(host.Context.Contact.Id, Ct);
        host.Reader.OfferCount = 2;
        var recovered = await service.PrepareAsync(host.Context.Contact.Id, Ct);
        Assert.True(recovered.Value!.RecoverOnly);
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id, first.Value!.RequestId,
            ArquitecturaBase.Domain.Results.Result.Failure<ArquitecturaBase.Application.Models.Assistant.AssistantOperationResponse>(WhatsAppAssistantErrors.StateConflict), Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked, Assert.Single(host.Requests.Rows).State);
        Assert.Equal(0, host.Assistant.Submits);
    }
}
