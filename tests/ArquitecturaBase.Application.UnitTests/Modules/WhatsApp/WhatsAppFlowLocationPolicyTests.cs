using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// W1-A: a form with a location question asks for it before its Flow; each location goes to the oldest send that waits for one,
// a tap on «Seguir sin ubicación» to its own send, and the Flow goes out once that is resolved.
public sealed class WhatsAppFlowLocationPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Actor = Guid.CreateVersion7();
    private static readonly Guid Contact = Guid.CreateVersion7();

    [Fact]
    public void A_form_without_a_location_opens_with_its_flow_as_before()
    {
        var session = Session("es");

        var opened = WhatsAppFlowLocationPolicy.Open(Projection(), session, Actor, Guid.CreateVersion7(), "Completá el formulario «Visita».", "Visita", Now);

        var flow = Assert.Single(opened);
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, "Completá el formulario «Visita».", session.Id), (flow.Kind, flow.Body, flow.FlowSessionId));
        Assert.Equal(WhatsAppFlowLocationState.None, session.LocationState);
    }

    [Fact]
    public void A_required_location_opens_with_the_request_and_the_flow_waits_with_its_body()
    {
        var session = Session("es");
        var requestId = Guid.CreateVersion7();

        var opened = WhatsAppFlowLocationPolicy.Open(Projection(location: true), session, Actor, requestId, "Hola, completalo hoy.", "Registro de visita", Now);

        var request = Assert.Single(opened);
        Assert.Equal((WhatsAppOutgoingMessageKind.LocationRequest, "Para completar «Registro de visita», mandanos tu ubicación.", requestId, Actor),
            (request.Kind, request.Body, request.RequestId, request.OperatorUserId));
        Assert.Equal((WhatsAppFlowLocationState.Requested, true, "Hola, completalo hoy."), (session.LocationState, session.LocationRequired, session.FlowBody));
    }

    [Fact]
    public void An_optional_location_adds_the_skip_button_in_the_language_of_the_form()
    {
        var session = Session("en");

        var opened = WhatsAppFlowLocationPolicy.Open(Projection(location: false), session, Actor, Guid.CreateVersion7(), "Complete the form “Visit log”.", "Visit log", Now);

        Assert.Equal([WhatsAppOutgoingMessageKind.LocationRequest, WhatsAppOutgoingMessageKind.QuickReplies], opened.Select(message => message.Kind));
        Assert.Equal("To fill in “Visit log”, send us your location.", opened[0].Body);
        Assert.Equal("The location is optional.", opened[1].Body);
        var button = Assert.Single(opened[1].QuickReplies);
        Assert.Equal(("Continue without it", WhatsAppFlowLocationReply.Id(session.Id)), (button.Title, button.Id));
        Assert.False(session.LocationRequired);
    }

    // The durable outgoing goes out by RequestedAtUtc and then by Id, a version 7 Guid that is not monotonic within a millisecond:
    // the skip button is requested a millisecond after its request, so the person never gets it first (person or assistant).
    [Fact]
    public void The_skip_button_is_requested_right_after_its_location_request()
    {
        var person = WhatsAppFlowLocationPolicy.Open(Projection(location: false), Session("es"), Actor, Guid.CreateVersion7(), "Completá.", "Visita", Now);
        var assistant = WhatsAppFlowLocationPolicy.OpenAssistant(Projection(location: false), AssistantSession(), Guid.CreateVersion7(), "Completá.",
            "Visita", Now, 3, "5491122334455", 7);

        Assert.All(new[] { person, assistant }, opened =>
            Assert.Equal((Now, Now.AddMilliseconds(1)), (opened[0].RequestedAtUtc, opened[1].RequestedAtUtc)));
    }

    [Fact]
    public void Each_location_completes_the_oldest_send_that_waits_for_one()
    {
        var older = Waiting(required: true, createdAt: Now);
        var newer = Waiting(required: true, createdAt: Now.AddMinutes(5));
        var first = Location("wamid.first", 1, Now.AddMinutes(6), -34.603722m, -58.381592m);
        var second = Location("wamid.second", 2, Now.AddMinutes(7), -31.4m, -64.18m);

        var resolved = WhatsAppFlowLocationPolicy.Resolve([newer, older], [second, first], Now.AddMinutes(8));

        Assert.Equal([older, newer], resolved);
        Assert.Equal((-34.603722m, older.Id), (older.LocationLatitude!.Value, first.FlowSessionId!.Value));
        Assert.Equal((-31.4m, newer.Id), (newer.LocationLatitude!.Value, second.FlowSessionId!.Value));
    }

    [Fact]
    public void A_location_sent_before_a_send_existed_or_after_it_was_resolved_changes_nothing()
    {
        var session = Waiting(required: true, createdAt: Now);
        var early = Location("wamid.early", 1, Now.AddMinutes(-1), -34.6m, -58.38m);
        var answer = Location("wamid.answer", 2, Now.AddMinutes(1), -34.603722m, -58.381592m);
        var again = Location("wamid.again", 3, Now.AddMinutes(2), -31.4m, -64.18m);

        var resolved = WhatsAppFlowLocationPolicy.Resolve([session], [early, answer, again], Now.AddMinutes(3));

        Assert.Equal([session], resolved);
        Assert.Equal((-34.603722m, "wamid.answer"), (session.LocationLatitude!.Value, session.LocationWaMessageId));
        Assert.Null(early.FlowSessionId);
        Assert.Null(again.FlowSessionId);
    }

    [Fact]
    public void The_skip_tap_resolves_its_own_optional_send_and_never_a_required_one()
    {
        var optional = Waiting(required: false, createdAt: Now);
        var required = Waiting(required: true, createdAt: Now);
        var skipOptional = Tap("wamid.skip1", 1, Now.AddMinutes(1), optional.Id);
        var skipRequired = Tap("wamid.skip2", 2, Now.AddMinutes(1), required.Id);
        var other = WhatsAppMessage.Inbound(Contact, "wamid.other", WhatsAppMessageKind.ButtonReply, "Sí", "FORM_ACTION:x", Now.AddMinutes(1));

        var resolved = WhatsAppFlowLocationPolicy.Resolve([optional, required], [skipOptional, skipRequired, other], Now.AddMinutes(2));

        Assert.Equal([optional], resolved);
        Assert.Equal(WhatsAppFlowLocationState.Skipped, optional.LocationState);
        Assert.Equal(WhatsAppFlowLocationState.Requested, required.LocationState);
        Assert.Equal(optional.Id, skipOptional.FlowSessionId);
        Assert.Null(skipRequired.FlowSessionId);
    }

    [Fact]
    public void An_expired_send_takes_no_location()
    {
        var session = Waiting(required: true, createdAt: Now);

        var resolved = WhatsAppFlowLocationPolicy.Resolve([session], [Location("wamid.late", 1, Now.AddHours(23), -34.6m, -58.38m)], Now.AddHours(24));

        Assert.Empty(resolved);
        Assert.Equal(WhatsAppFlowLocationState.Requested, session.LocationState);
    }

    [Fact]
    public void Once_resolved_the_flow_goes_out_with_the_body_of_the_send_and_the_reply_takes_the_point()
    {
        var session = Session("es");
        var request = WhatsAppFlowLocationPolicy.Open(Projection(location: true), session, Actor, Guid.CreateVersion7(), "Completá el formulario.", "Visita", Now)[0];
        Assert.Throws<InvalidOperationException>(() => WhatsAppFlowLocationPolicy.Flow(session, request, Now));
        session.ReceiveLocation(-34.603722m, -58.381592m, "wamid.answer", Now.AddMinutes(1));

        var flow = WhatsAppFlowLocationPolicy.Flow(session, request, Now.AddMinutes(1));

        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, "Completá el formulario.", Actor, session.Id), (flow.Kind, flow.Body, flow.OperatorUserId, flow.FlowSessionId));
        Assert.Equal("""{"latitude":-34.603722,"longitude":-58.381592}""",
            WhatsAppFlowLocationPolicy.Answer(session.LocationState, session.LocationLatitude, session.LocationLongitude));
        Assert.Null(WhatsAppFlowLocationPolicy.Answer(WhatsAppFlowLocationState.Skipped, null, null));
    }

    // The assistant also sends forms with a location (asked by the user on 2026-11-11). Without one, its send opens with its Flow
    // as before; with one, with the request and the skip button, all of them the assistant's, with the recipient and revisions it
    // sealed.
    [Fact]
    public void An_assistant_send_without_a_location_opens_with_its_flow_as_before()
    {
        var session = AssistantSession();

        var flow = Assert.Single(WhatsAppFlowLocationPolicy.OpenAssistant(Projection(), session, Guid.CreateVersion7(), "Completá el formulario «Visita».",
            "Visita", Now, 3, "5491122334455", 7));

        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, WhatsAppMessageOrigin.Assistant, (Guid?)session.UserId, (long?)7, session.Id),
            (flow.Kind, flow.Origin, flow.RecipientUserId, flow.AssistantModeRevision, flow.FlowSessionId!.Value));
        Assert.Equal(WhatsAppFlowLocationState.None, session.LocationState);
    }

    [Fact]
    public void An_assistant_send_with_a_location_opens_with_the_assistant_request_and_its_skip_button()
    {
        var session = AssistantSession();
        var requestId = Guid.CreateVersion7();

        var opened = WhatsAppFlowLocationPolicy.OpenAssistant(Projection(location: false), session, requestId,
            "Completá el formulario «Registro de visita».", "Registro de visita", Now, 3, "5491122334455", 7);

        Assert.Equal([WhatsAppOutgoingMessageKind.LocationRequest, WhatsAppOutgoingMessageKind.QuickReplies], opened.Select(message => message.Kind));
        Assert.All(opened, message => Assert.Equal((WhatsAppMessageOrigin.Assistant, (Guid?)null, (Guid?)session.UserId, (long?)3, (long?)7),
            (message.Origin, message.OperatorUserId, message.RecipientUserId, message.RecipientLinkRevision, message.AssistantModeRevision)));
        Assert.Equal((requestId, "Para completar «Registro de visita», mandanos tu ubicación."), (opened[0].RequestId, opened[0].Body));
        Assert.Equal(WhatsAppFlowLocationReply.Id(session.Id), Assert.Single(opened[1].QuickReplies).Id);
        Assert.Equal((WhatsAppFlowLocationState.Requested, "Completá el formulario «Registro de visita»."), (session.LocationState, session.FlowBody));
    }

    // The Flow continues the request that opened its send: the assistant's with the recipient and revisions it sealed, which the
    // reservation checks again, so a conversation taken in the meantime does not let it out.
    [Fact]
    public void Once_resolved_the_assistant_flow_continues_its_request_with_what_it_sealed()
    {
        var session = AssistantSession();
        var request = WhatsAppFlowLocationPolicy.OpenAssistant(Projection(location: true), session, Guid.CreateVersion7(), "Completá el formulario.",
            "Visita", Now, 3, "5491122334455", 7)[0];
        session.ReceiveLocation(-34.603722m, -58.381592m, "wamid.answer", Now.AddMinutes(1));

        var flow = WhatsAppFlowLocationPolicy.Flow(session, request, Now.AddMinutes(1));

        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, WhatsAppMessageOrigin.Assistant, (Guid?)null, (Guid?)session.UserId, (long?)3, "5491122334455", (long?)7),
            (flow.Kind, flow.Origin, flow.OperatorUserId, flow.RecipientUserId, flow.RecipientLinkRevision, flow.RecipientWaId, flow.AssistantModeRevision));
        Assert.Equal(("Completá el formulario.", session.Id), (flow.Body, flow.FlowSessionId!.Value));
        Assert.NotEqual(request.RequestId, flow.RequestId);
        var other = Session("es");
        WhatsAppFlowLocationPolicy.Open(Projection(location: true), other, Actor, Guid.CreateVersion7(), "Completá.", "Visita", Now);
        other.ReceiveLocation(-34.6m, -58.38m, "wamid.other", Now.AddMinutes(1));
        Assert.Throws<InvalidOperationException>(() => WhatsAppFlowLocationPolicy.Flow(other, request, Now.AddMinutes(1)));
    }

    private static WhatsAppFlowSession Session(string culture) => WhatsAppFlowSession.Create(Contact, 2, Guid.CreateVersion7(), Actor, Guid.CreateVersion7(),
        Guid.CreateVersion7(), 1, Guid.CreateVersion7(), culture, "{}", "VIS", new('a', 64), new('b', 64), Now.AddHours(24));

    private static WhatsAppFlowSession AssistantSession() => WhatsAppFlowSession.CreateAssistant(Contact, 3, Guid.CreateVersion7(), Guid.CreateVersion7(),
        Guid.CreateVersion7(), 1, Guid.CreateVersion7(), "es", "{}", "VIS", new('a', 64), new('b', 64), Now.AddHours(24));

    private static WhatsAppFlowSession Waiting(bool required, DateTime createdAt)
    {
        var session = Session("es");
        session.RequestLocation(required, "Completá el formulario.");
        typeof(WhatsAppFlowSession).GetProperty(nameof(WhatsAppFlowSession.CreatedAtUtc))!.SetValue(session, createdAt);
        return session;
    }

    private static WhatsAppMessage Location(string id, long sequence, DateTime at, decimal latitude, decimal longitude)
    {
        var message = WhatsAppMessage.Inbound(Contact, id, WhatsAppMessageKind.Location, null, null, at);
        message.RecordLocation(latitude, longitude);
        message.SetInboundSequence(sequence);
        return message;
    }

    private static WhatsAppMessage Tap(string id, long sequence, DateTime at, Guid sessionId)
    {
        var message = WhatsAppMessage.Inbound(Contact, id, WhatsAppMessageKind.ButtonReply, "Seguir sin ubicación", WhatsAppFlowLocationReply.Id(sessionId), at);
        message.SetInboundSequence(sequence);
        return message;
    }

    private static WhatsAppFlowProjection Projection(bool? location = null)
    {
        var text = Guid.CreateVersion7();
        List<WhatsAppFlowProjectionField> mapping = [new(text, "field_" + text.ToString("N"), "shortText", true, 80, [])];
        if (location is { } required)
        {
            var id = Guid.CreateVersion7();
            mapping.Add(new(id, "field_" + id.ToString("N"), "location", required, null, []));
        }
        return new(1, "{}", new string('c', 64), "r13", 1, "es", mapping);
    }
}
