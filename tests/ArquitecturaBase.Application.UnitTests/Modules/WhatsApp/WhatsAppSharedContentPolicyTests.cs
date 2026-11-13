using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// W3 (UC0, UC2 to UC5): after W1-A, a location nothing took and a contact go to the oldest open send whose form receives them, whose
// sender can still send and that has not taken one of that kind yet; what no send can use stays in the chat.
public sealed class WhatsAppSharedContentPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 15, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Actor = Guid.CreateVersion7();
    private readonly WhatsAppContact _contact = Linked();

    [Fact]
    public void Each_location_goes_to_the_oldest_send_that_receives_it_the_first_one_counts_and_the_rest_stays_in_the_chat()
    {
        var older = Send(Now.AddMinutes(-10), Location);
        var newer = Send(Now.AddMinutes(-5), Location);
        var first = Shared(1, location: "Av. Pellegrini 1200, Rosario");
        var second = Shared(2, latitude: -32.947102m, longitude: -60.638955m);
        var third = Shared(3, location: "Bv. Oroño 50");

        var takes = WhatsAppSharedContentPolicy.Resolve([newer, older], [third, second, first], _contact, Now);

        Assert.Equal([(older.Session, first.Id), (newer.Session, second.Id)], takes.Select(take => (take.Send.Session, Message(take, [first, second, third]))));
        Assert.Equal("Av. Pellegrini 1200, Rosario", Assert.Single(takes[0].Values).Value);
        Assert.Equal(("Dirección de entrega", "-32.947102, -60.638955"), (takes[1].Values[0].Label, takes[1].Values[0].Value));
        Assert.Equal((older.Session.Id, newer.Session.Id), (first.FlowSessionId, second.FlowSessionId));
        Assert.Null(third.FlowSessionId);
        Assert.Equal([takes[0].Values[0].FieldId], older.Session.SharedLocation!.FieldIds);
    }

    // A1 and A2: W1-A goes first, and a send that was waiting for its location when a contact arrived had not gone out: it takes nothing
    // of it, even when that location resolves it in the same pass.
    [Fact]
    public void W1A_takes_its_location_first_and_a_send_that_waited_for_it_takes_no_contact()
    {
        var waiting = Send(Now.AddMinutes(-10), ContactName, ContactPhone);
        waiting.Session.RequestLocation(true, "Completá el formulario.");
        var open = Send(Now.AddMinutes(-5), Location, ContactName, ContactPhone);
        var location = Shared(1, latitude: -34.603722m, longitude: -58.381592m);
        var contact = Shared(2, name: "Ana Gómez", phones: ["+54 9 341 555-0101", "+54 341 444-0202"]);

        Assert.Equal([waiting.Session], WhatsAppFlowLocationPolicy.Resolve([waiting.Session], [location, contact], Now));
        var takes = WhatsAppSharedContentPolicy.Resolve([waiting, open], [location, contact], _contact, Now);

        var take = Assert.Single(takes);
        Assert.Equal((open.Session, WhatsAppMessageKind.Contact), (take.Send.Session, take.Kind));
        Assert.Equal(["Ana Gómez", "+54 9 341 555-0101"], take.Values.Select(value => value.Value));
        Assert.Equal((waiting.Session.Id, open.Session.Id), (location.FlowSessionId, contact.FlowSessionId));
        Assert.Null(open.Session.SharedLocation);
    }

    // A6: the operator's send takes something only while the conversation is in Atención manual with the revision it sealed; the
    // assistant's, while it is in Asistente with the revision, link and number it sealed. Otherwise it goes to the next send or stays.
    [Fact]
    public void A_send_whose_sender_can_no_longer_send_takes_nothing_and_it_goes_to_the_next_one()
    {
        _contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var operatorSend = Send(Now.AddMinutes(-10), Location);
        var stale = Send(Now.AddMinutes(-8), Location, assistant: true, modeRevision: _contact.ModeRevision - 1);
        var assistantSend = Send(Now.AddMinutes(-5), Location, assistant: true);
        var location = Shared(1, location: "Av. Pellegrini 1200");

        var take = Assert.Single(WhatsAppSharedContentPolicy.Resolve([operatorSend, stale, assistantSend], [location], _contact, Now));

        Assert.Equal(assistantSend.Session, take.Send.Session);
        _contact.ChangeMode(WhatsAppConversationMode.Manual);
        var later = Shared(2, location: "Bv. Oroño 50");
        Assert.Empty(WhatsAppSharedContentPolicy.Resolve([stale, assistantSend], [later], _contact, Now));
        Assert.Null(later.FlowSessionId);
    }

    // A3 and A4: a contact without a phone fills only its name, so a send that receives only the phone does not take it; a value is cut
    // at the length of its question.
    [Fact]
    public void A_contact_without_a_phone_fills_only_the_name_and_a_value_is_cut_at_the_length_of_its_question()
    {
        var phoneOnly = Send(Now.AddMinutes(-10), ContactPhone);
        var nameAndPhone = Send(Now.AddMinutes(-5), ContactName, ContactPhone, maxLength: 8);
        var contact = Shared(1, name: "Ana Gómez de la Fuente", phones: []);

        var take = Assert.Single(WhatsAppSharedContentPolicy.Resolve([phoneOnly, nameAndPhone], [contact], _contact, Now));

        Assert.Equal(nameAndPhone.Session, take.Send.Session);
        Assert.Equal(("Recibió", "Ana Góme"), (Assert.Single(take.Values).Label, take.Values[0].Value));
        Assert.Equal("Puse el contacto en «Recibió». Revisá el formulario «Registrar entrega» y confirmalo.",
            WhatsAppSharedContentPolicy.Body(nameAndPhone.Session, [take]));
        Assert.Null(phoneOnly.Session.SharedContact);
    }

    // A5: one resend per send and pass, with a message that names what this pass filled and the form, from the sender of the first
    // message of the send and with its seal, marked as a resend.
    [Fact]
    public void A_location_and_a_contact_in_one_pass_make_one_resend_that_continues_the_first_message()
    {
        _contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var send = Send(Now.AddMinutes(-10), Location, ContactName, ContactPhone, assistant: true, culture: "en");
        var location = Shared(1, location: "1200 Pellegrini Ave");
        var contact = Shared(2, name: "Ana Gómez", phones: ["+54 9 341 555-0101"]);

        var takes = WhatsAppSharedContentPolicy.Resolve([send], [contact, location], _contact, Now);
        var resend = WhatsAppFlowLocationPolicy.Resend(send.Session, send.First, WhatsAppSharedContentPolicy.Body(send.Session, takes), Now);

        Assert.Equal("I put your location in “Delivery address”. I put the contact in “Received by” and “Phone of who receives”. "
            + "Check the form “Register delivery” and confirm it.", resend.Body);
        Assert.Equal((WhatsAppOutgoingMessageKind.Flow, true, WhatsAppMessageOrigin.Assistant, send.First.RecipientUserId, send.First.RecipientLinkRevision,
            send.First.RecipientWaId, send.First.AssistantModeRevision, send.Session.Id),
            (resend.Kind, resend.IsResend, resend.Origin, resend.RecipientUserId, resend.RecipientLinkRevision, resend.RecipientWaId,
                resend.AssistantModeRevision, resend.FlowSessionId!.Value));
        Assert.NotEqual(send.First.RequestId, resend.RequestId);
        Assert.Throws<InvalidOperationException>(() => WhatsAppFlowLocationPolicy.Resend(send.Session, resend, "Again.", Now));
        var operatorSend = Send(Now.AddMinutes(-10), Location);
        var operatorResend = WhatsAppFlowLocationPolicy.Resend(operatorSend.Session, operatorSend.First, "Puse tu ubicación.", Now);
        Assert.Equal((WhatsAppMessageOrigin.Conversation, (Guid?)Actor, true), (operatorResend.Origin, operatorResend.OperatorUserId, operatorResend.IsResend));
    }

    // A2 and A6: a send sealed for another account or link, or an assistant send for another link or number, takes nothing, and the
    // next one takes it.
    [Theory]
    [InlineData("account")]
    [InlineData("link")]
    [InlineData("recipient link")]
    [InlineData("number")]
    public void A_send_sealed_for_another_account_link_or_number_takes_nothing(string sealedFor)
    {
        _contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var stale = Send(Now.AddMinutes(-10), Location, assistant: true);
        var current = Send(Now.AddMinutes(-5), Location, assistant: true);
        object target = sealedFor is "account" or "link" ? stale.Session : stale.First;
        var (property, value) = sealedFor switch
        {
            "account" => (nameof(WhatsAppFlowSession.UserId), (object)Guid.CreateVersion7()),
            "link" => (nameof(WhatsAppFlowSession.LinkRevision), (object)(_contact.LinkRevision + 1)),
            "recipient link" => (nameof(WhatsAppOutgoingMessage.RecipientLinkRevision), (object)(_contact.LinkRevision + 1)),
            _ => (nameof(WhatsAppOutgoingMessage.RecipientWaId), (object)"5493415559999"),
        };
        target.GetType().GetProperty(property)!.SetValue(target, value);

        var take = Assert.Single(WhatsAppSharedContentPolicy.Resolve([stale, current], [Shared(1, location: "Av. Pellegrini 1200")], _contact, Now));

        Assert.Equal(current.Session, take.Send.Session);
        Assert.Null(stale.Session.SharedLocation);
    }

    private const string Location = WhatsAppSharedContentPolicy.Location;
    private const string ContactName = WhatsAppSharedContentPolicy.ContactName;
    private const string ContactPhone = WhatsAppSharedContentPolicy.ContactPhone;

    private static readonly Dictionary<string, (string Es, string En)> Labels = new()
    {
        [Location] = ("Dirección de entrega", "Delivery address"),
        [ContactName] = ("Recibió", "Received by"),
        [ContactPhone] = ("Teléfono de quien recibe", "Phone of who receives"),
    };

    private static WhatsAppContact Linked()
    {
        var contact = WhatsAppContact.Create("5493415550000", "AR.1234567890", "Persona ficticia", Now.AddDays(-1));
        contact.LinkUser(Guid.CreateVersion7());
        return contact;
    }

    private WhatsAppSharedContentSend Send(DateTime createdAt, string receives, string? also = null, string? third = null, bool assistant = false,
        long? modeRevision = null, int maxLength = 80, string culture = "es")
    {
        var userId = _contact.UserId!.Value;
        var names = JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = "Registrar entrega", ["en"] = "Register delivery" });
        var session = assistant
            ? WhatsAppFlowSession.CreateAssistant(_contact.Id, _contact.LinkRevision, userId, Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
                Guid.CreateVersion7(), culture, names, "ENT", new('a', 64), new('b', 64), Now.AddHours(20))
            : WhatsAppFlowSession.Create(_contact.Id, _contact.LinkRevision, userId, Actor, Guid.CreateVersion7(), Guid.CreateVersion7(), 1,
                Guid.CreateVersion7(), culture, names, "ENT", new('a', 64), new('b', 64), Now.AddHours(20));
        typeof(WhatsAppFlowSession).GetProperty(nameof(WhatsAppFlowSession.CreatedAtUtc))!.SetValue(session, createdAt);
        var revision = modeRevision ?? _contact.ModeRevision;
        var first = assistant
            ? WhatsAppOutgoingMessage.CreateAssistantFlow(userId, _contact.Id, Guid.CreateVersion7(), "Completá el formulario.", session.Id, createdAt,
                _contact.LinkRevision, _contact.WaId!, revision)
            : WhatsAppOutgoingMessage.CreateFlow(Actor, _contact.Id, Guid.CreateVersion7(), "Completá el formulario.", session.Id, createdAt);
        first.AttachFlowSession(session);
        string[] chosen = [receives, .. new[] { also, third }.OfType<string>()];
        var plain = Guid.CreateVersion7();
        List<WhatsAppFlowProjectionField> mapping = [new(plain, "field_" + plain.ToString("N"), "shortText", true, 80, [])];
        mapping.AddRange(chosen.Select(value =>
        {
            var id = Guid.CreateVersion7();
            return new WhatsAppFlowProjectionField(id, "field_" + id.ToString("N"), "shortText", false, maxLength, [], Receives: value);
        }));
        var json = JsonSerializer.Serialize(new { screens = new[] { new { id = "FORM", children = mapping.Select(field => new
        {
            name = field.Key, label = field.Receives is { } value ? culture == "es" ? Labels[value].Es : Labels[value].En : "Cliente",
        }) } } });
        return new(session, first, new(1, json, new string('c', 64), WhatsAppFlowProfile.Revision, 1, culture, mapping), revision);
    }

    private WhatsAppMessage Shared(long sequence, string? location = null, decimal? latitude = null, decimal? longitude = null, string? name = null,
        string[]? phones = null)
    {
        var at = Now.AddMinutes(-1).AddSeconds(sequence);
        var message = WhatsAppMessage.Inbound(_contact.Id, "wamid.shared" + sequence, phones is null ? WhatsAppMessageKind.Location : WhatsAppMessageKind.Contact,
            null, null, at);
        if (phones is null) message.RecordLocation(latitude ?? -32.9m, longitude ?? -60.6m, location);
        else message.RecordContact(name, phones);
        message.SetInboundSequence(sequence);
        return message;
    }

    private static Guid Message(WhatsAppSharedContentTake take, IEnumerable<WhatsAppMessage> messages) =>
        messages.Single(message => message.FlowSessionId == take.Send.Session.Id && message.Kind == take.Kind).Id;
}
