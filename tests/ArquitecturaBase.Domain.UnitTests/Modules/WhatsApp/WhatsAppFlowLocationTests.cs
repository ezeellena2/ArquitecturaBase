using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

// W1-A: a form with a location question asks for it before its Flow, and the send keeps the first answer that arrives: the location
// or, when the question is optional, «Seguir sin ubicación».
public sealed class WhatsAppFlowLocationTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);

    private static WhatsAppFlowSession Session() => WhatsAppFlowSession.Create(Guid.NewGuid(), 2, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        Guid.NewGuid(), 1, Guid.NewGuid(), "es", "{}", "V", new('a', 64), new('b', 64), Now.AddHours(24));

    [Fact]
    public void A_send_asks_for_the_location_once_and_keeps_the_body_of_its_flow_message()
    {
        var session = Session();
        Assert.False(session.AwaitsLocation(Now));

        session.RequestLocation(required: true, "Completá el formulario «Visita».");

        Assert.Equal((WhatsAppFlowLocationState.Requested, true, "Completá el formulario «Visita»."),
            (session.LocationState, session.LocationRequired, session.FlowBody));
        Assert.True(session.AwaitsLocation(Now));
        Assert.Throws<InvalidOperationException>(() => session.RequestLocation(required: false, "Otro"));
    }

    [Fact]
    public void The_first_location_completes_the_request_and_another_changes_nothing()
    {
        var session = Session();
        session.RequestLocation(required: false, "Completá el formulario.");

        Assert.True(session.ReceiveLocation(-34.603722m, -58.381592m, "wamid.first", Now.AddMinutes(1)));
        Assert.False(session.ReceiveLocation(-31.4m, -64.18m, "wamid.second", Now.AddMinutes(2)));
        Assert.False(session.SkipLocation("wamid.skip", Now.AddMinutes(3)));

        Assert.Equal((WhatsAppFlowLocationState.Received, -34.603722m, -58.381592m, "wamid.first", Now.AddMinutes(1)),
            (session.LocationState, session.LocationLatitude, session.LocationLongitude, session.LocationWaMessageId, session.LocationResolvedAtUtc));
        Assert.False(session.AwaitsLocation(Now.AddMinutes(4)));
    }

    [Fact]
    public void Only_an_optional_location_can_be_skipped_and_a_later_location_changes_nothing()
    {
        var required = Session();
        required.RequestLocation(required: true, "Completá.");
        var optional = Session();
        optional.RequestLocation(required: false, "Completá.");

        Assert.False(required.SkipLocation("wamid.skip", Now));
        Assert.True(optional.SkipLocation("wamid.skip", Now));
        Assert.False(optional.ReceiveLocation(-34.603722m, -58.381592m, "wamid.late", Now));

        Assert.Equal(WhatsAppFlowLocationState.Requested, required.LocationState);
        Assert.Equal((WhatsAppFlowLocationState.Skipped, (decimal?)null, "wamid.skip"),
            (optional.LocationState, optional.LocationLatitude, optional.LocationWaMessageId));
    }

    [Fact]
    public void An_expired_send_awaits_no_location()
    {
        var session = Session();
        session.RequestLocation(required: true, "Completá.");

        Assert.False(session.AwaitsLocation(Now.AddHours(24)));
        Assert.False(session.ReceiveLocation(-34.603722m, -58.381592m, "wamid.late", Now.AddHours(24)));
        Assert.Equal(WhatsAppFlowLocationState.Requested, session.LocationState);
    }

    [Theory]
    [InlineData("-90.000001", "0")]
    [InlineData("0", "180.000001")]
    [InlineData("0.0000001", "0")]
    public void A_location_out_of_range_or_with_more_than_six_decimals_is_refused(string latitude, string longitude)
    {
        var session = Session();
        session.RequestLocation(required: true, "Completá.");

        Assert.Throws<ArgumentOutOfRangeException>(() => session.ReceiveLocation(decimal.Parse(latitude, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(longitude, System.Globalization.CultureInfo.InvariantCulture), "wamid.bad", Now));
    }

    [Fact]
    public void The_request_and_the_skip_button_go_out_tied_to_their_send()
    {
        var sessionId = Guid.CreateVersion7();
        var contactId = Guid.NewGuid();
        var actor = Guid.NewGuid();

        var request = WhatsAppOutgoingMessage.CreateLocationRequest(actor, contactId, Guid.NewGuid(), "Para completar «Visita», mandanos tu ubicación.", sessionId, Now);
        var skip = WhatsAppOutgoingMessage.CreateLocationSkip(actor, contactId, Guid.NewGuid(), "La ubicación es opcional.", "Seguir sin ubicación", sessionId, Now);

        Assert.Equal((WhatsAppOutgoingMessageKind.LocationRequest, sessionId, actor, WhatsAppMessageOrigin.Conversation),
            (request.Kind, request.FlowSessionId, request.OperatorUserId, request.Origin));
        Assert.Equal((WhatsAppOutgoingMessageKind.QuickReplies, sessionId), (skip.Kind, skip.FlowSessionId));
        var button = Assert.Single(skip.QuickReplies);
        Assert.Equal("Seguir sin ubicación", button.Title);
        Assert.True(WhatsAppFlowLocationReply.TryParse(button.Id, out var parsed));
        Assert.Equal(sessionId, parsed);
        Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateLocationSkip(actor, contactId, Guid.NewGuid(), "La ubicación es opcional.",
            "Seguir sin la ubicación", sessionId, Now));
    }

    // The assistant also sends forms with a location (asked by the user on 2026-11-11): its request and its skip button are the
    // assistant's, without an operator, with the recipient and revisions it sealed, and tied to their send like a person's.
    [Fact]
    public void The_assistant_request_and_skip_button_go_out_as_the_assistant_tied_to_their_send()
    {
        var sessionId = Guid.CreateVersion7();
        var contactId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = WhatsAppOutgoingMessage.CreateAssistantLocationRequest(userId, contactId, Guid.NewGuid(),
            "Para completar «Visita», mandanos tu ubicación.", sessionId, Now, 3, "5491122334455", 7);
        var skip = WhatsAppOutgoingMessage.CreateAssistantLocationSkip(userId, contactId, Guid.NewGuid(), "La ubicación es opcional.",
            "Seguir sin ubicación", sessionId, Now, 3, "5491122334455", 7);

        Assert.All(new[] { request, skip }, outgoing => Assert.Equal(
            (WhatsAppMessageOrigin.Assistant, (Guid?)null, (Guid?)userId, (Guid?)sessionId, (long?)3, "5491122334455", (long?)7),
            (outgoing.Origin, outgoing.OperatorUserId, outgoing.RecipientUserId, outgoing.FlowSessionId, outgoing.RecipientLinkRevision,
                outgoing.RecipientWaId, outgoing.AssistantModeRevision)));
        Assert.Equal((WhatsAppOutgoingMessageKind.LocationRequest, WhatsAppOutgoingMessageKind.QuickReplies), (request.Kind, skip.Kind));
        Assert.Equal(WhatsAppFlowLocationReply.Id(sessionId), Assert.Single(skip.QuickReplies).Id);
        Assert.Throws<ArgumentException>(() => WhatsAppOutgoingMessage.CreateAssistantLocationSkip(userId, contactId, Guid.NewGuid(),
            "La ubicación es opcional.", "Seguir sin la ubicación", sessionId, Now, 3, "5491122334455", 7));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("FORM_ACTION:0123")]
    [InlineData("FLOW_LOCATION_SKIP:not-a-guid")]
    public void A_button_that_is_not_a_skip_is_not_read_as_one(string? replyId) =>
        Assert.False(WhatsAppFlowLocationReply.TryParse(replyId, out _));

    [Fact]
    public void An_inbound_location_keeps_its_point_once_and_another_kind_takes_none()
    {
        var location = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.location", WhatsAppMessageKind.Location, null, null, Now);
        var text = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.text", WhatsAppMessageKind.Text, "hola", null, Now);

        location.RecordLocation(-34.603722m, -58.381592m);

        Assert.Equal((-34.603722m, -58.381592m), (location.Latitude, location.Longitude));
        Assert.Throws<InvalidOperationException>(() => location.RecordLocation(-31.4m, -64.18m));
        Assert.Throws<InvalidOperationException>(() => text.RecordLocation(-31.4m, -64.18m));
    }

    [Fact]
    public void The_sent_skip_message_keeps_its_button_in_the_chat_once()
    {
        var sent = WhatsAppMessage.Outbound(Guid.NewGuid(), "wamid.skip", WhatsAppMessageKind.Interactive, "La ubicación es opcional.", Now);

        sent.RecordReplyTitles(["Seguir sin ubicación"]);

        Assert.Equal(["Seguir sin ubicación"], System.Text.Json.JsonSerializer.Deserialize<string[]>(sent.QuickReplyTitlesJson!)!);
        Assert.Throws<InvalidOperationException>(() => sent.RecordReplyTitles(["Otra"]));
        Assert.Throws<InvalidOperationException>(() => WhatsAppMessage.Outbound(Guid.NewGuid(), "wamid.text", WhatsAppMessageKind.Text, "Hola", Now)
            .RecordReplyTitles(["Seguir sin ubicación"]));
    }

    [Fact]
    public void A_location_or_a_skip_tap_attaches_to_the_send_it_resolved()
    {
        var sessionId = Guid.CreateVersion7();
        var location = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.location", WhatsAppMessageKind.Location, null, null, Now);
        var tap = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.tap", WhatsAppMessageKind.ButtonReply, "Seguir sin ubicación",
            WhatsAppFlowLocationReply.Id(sessionId), Now);
        var request = WhatsAppMessage.Outbound(Guid.NewGuid(), "wamid.request", WhatsAppMessageKind.LocationRequest, "Mandanos tu ubicación.", Now);

        location.AttachFlowSession(sessionId);
        tap.AttachFlowSession(sessionId);
        request.AttachFlowSession(sessionId);

        Assert.Equal([sessionId, sessionId, sessionId], new[] { location.FlowSessionId, tap.FlowSessionId, request.FlowSessionId });
    }
}
