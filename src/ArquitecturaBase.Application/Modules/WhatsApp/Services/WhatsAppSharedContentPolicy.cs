using System.Text.Json;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// W3 (UC1-A), after W1-A: a location that nothing took, and a contact, go to the oldest open send whose projection has a question that
/// receives them, that has not taken one of that kind yet and whose sender can still send; what no send can use stays in the chat. It
/// decides without AI and changes the entities (the send takes it, the message attaches to it); the caller passes only the sends whose
/// first message would pass the reservation (<see cref="WhatsAppSharedContentGuard"/>), protects what was written, queues one resend per
/// send and saves.
/// </summary>
internal static class WhatsAppSharedContentPolicy
{
    public const string Location = "location";
    public const string ContactName = "contactName";
    public const string ContactPhone = "contactPhone";

    public static IReadOnlyList<WhatsAppSharedContentTake> Resolve(IReadOnlyList<WhatsAppSharedContentSend> sends, IReadOnlyList<WhatsAppMessage> shared,
        WhatsAppContact contact, DateTime nowUtc)
    {
        var takes = new List<WhatsAppSharedContentTake>();
        var oldestFirst = sends.OrderBy(send => send.Session.CreatedAtUtc).ThenBy(send => send.Session.Id).ToArray();
        foreach (var message in shared.Where(message => message.FlowSessionId is null && message.Direction == WhatsAppMessageDirection.Inbound
                     && message.Kind is WhatsAppMessageKind.Location or WhatsAppMessageKind.Contact)
                     .OrderBy(message => message.InboundSequence).ThenBy(message => message.Id))
        {
            foreach (var send in oldestFirst.Where(send => CanSend(send, contact)))
            {
                // A3: a send that would fill none of its questions with it does not take it: it goes to the next one that can use it.
                var values = Values(send.Projection, message);
                if (values.Count == 0 || !send.Session.TakeShared(message.Kind, values.Select(value => value.FieldId).ToArray(), message.OccurredAtUtc, nowUtc))
                    continue;
                message.AttachFlowSession(send.Session.Id);
                takes.Add(new(send, message.Kind, values));
                break;
            }
        }
        return takes;
    }

    /// <summary>
    /// The message of the resend, in the language of the send: the questions this pass filled, the location before the contact, and the
    /// form («Puse tu ubicación en «Dirección de entrega». Revisá el formulario «Registrar entrega» y confirmalo.»).
    /// </summary>
    public static string Body(WhatsAppFlowSession session, IReadOnlyList<WhatsAppSharedContentTake> takes)
    {
        var culture = session.Culture;
        var parts = takes.OrderBy(take => take.Kind == WhatsAppMessageKind.Location ? 0 : 1).Select(take => take.Values.Select(value => value.Label).ToArray() switch
        {
            [var label] when take.Kind == WhatsAppMessageKind.Location => DisplayNumbers.Format(culture, FlowTexts.Get("SharedLocationPut", culture), label),
            [var label] => DisplayNumbers.Format(culture, FlowTexts.Get("SharedContactPutOne", culture), label),
            [var name, var phone] => DisplayNumbers.Format(culture, FlowTexts.Get("SharedContactPutTwo", culture), name, phone),
            _ => throw new InvalidOperationException("A shared location fills one question and a contact one or two."),
        }).ToList();
        var names = JsonSerializer.Deserialize<Dictionary<string, string>>(session.NameJson, JsonSerializerOptions.Web);
        if (names is null || !names.TryGetValue(culture, out var formName)) throw new InvalidOperationException("The send keeps the name of its form in its language.");
        parts.Add(DisplayNumbers.Format(culture, FlowTexts.Get("SharedReview", culture), formName));
        return string.Join(" ", parts);
    }

    // A6: the resend goes out as any message of its sender, so a send whose sender can no longer send takes nothing: the operator's while
    // the conversation is in Atención manual with the revision its send sealed; the assistant's while it is in Asistente with the revision,
    // link and number it sealed. A send of an account the number no longer has is not open (A2).
    private static bool CanSend(WhatsAppSharedContentSend send, WhatsAppContact contact) =>
        contact.UserId == send.Session.UserId && contact.LinkRevision == send.Session.LinkRevision && contact.ModeRevision == send.ModeRevision
        && (send.First.Origin == WhatsAppMessageOrigin.Assistant
            ? contact.Mode == WhatsAppConversationMode.Assistant && send.First.AssistantModeRevision == send.ModeRevision
                && send.First.RecipientLinkRevision == contact.LinkRevision && send.First.RecipientWaId == contact.WaId
            : send.First.Origin == WhatsAppMessageOrigin.Conversation && contact.Mode == WhatsAppConversationMode.Manual);

    // A4: the address of a place or, without one, its point; of a contact, the name and the first phone of its first card, as they came. A
    // value is cut at the length of its question and what the prefill would refuse is not written, so a contact without a name or a
    // phone fills only what it brings.
    private static List<WhatsAppFlowSharedValue> Values(WhatsAppFlowProjection projection, WhatsAppMessage message)
    {
        if (!WhatsAppFlowProfile.PrefillRevisions.Contains(projection.ProfileRevision)) return [];
        (string Receives, string? Text)[] written = message.Kind == WhatsAppMessageKind.Location
            ? [(Location, message.LocationAddress ?? (message.Latitude is { } latitude && message.Longitude is { } longitude
                ? new StructuredRecordLocationValue(latitude, longitude).Coordinates() : null))]
            : [(ContactName, message.ContactName), (ContactPhone, FirstPhone(message.ContactPhonesJson))];
        var values = new List<WhatsAppFlowSharedValue>();
        foreach (var (receives, text) in written)
        {
            if (string.IsNullOrWhiteSpace(text) || projection.Mapping.FirstOrDefault(field => field.Receives == receives) is not { } field
                || Cut(field, text) is not { } value || WhatsAppFlowMediaPolicy.QuestionLabel(projection, field.FieldId) is not { Length: > 0 } label) continue;
            values.Add(new(field.FieldId, label, value));
        }
        return values;
    }

    private static string? Cut(WhatsAppFlowProjectionField field, string text)
    {
        if (field.MaxLength is not { } maxLength || StorableText.Clean(text, maxLength) is not { } value) return null;
        var decoded = WhatsAppFlowValueCodec.Decode(new(field.FieldId, field.Key, field.Type, false, field.MaxLength, field.Choices, field.Files), value);
        return decoded.IsSuccess && decoded.Value.IsPresent ? value : null;
    }

    private static string? FirstPhone(string? phonesJson)
    {
        if (phonesJson is null) return null;
        try { return JsonSerializer.Deserialize<string[]>(phonesJson)?.FirstOrDefault(); }
        catch (JsonException) { return null; }
    }
}
