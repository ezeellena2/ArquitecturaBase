using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Lee el formato de Meta del webhook <c>messages</c> (su referencia y la de los BSUID): <c>entry[].changes[]</c>, con
/// <c>field</c> "messages" y, en <c>value</c>, <c>metadata.phone_number_id</c>, <c>contacts</c> (<c>wa_id</c>,
/// <c>user_id</c> y <c>profile.name</c>), <c>messages</c> (<c>from</c>, <c>from_user_id</c>, <c>id</c>,
/// <c>timestamp</c> en segundos Unix y el contenido según <c>type</c>) y <c>statuses</c> (<c>id</c>, <c>status</c>,
/// <c>timestamp</c> y <c>errors</c>). Lo que no es de este número o no se entiende se deja afuera sin frenar el resto:
/// un error haría que Meta reintente durante días algo que nunca se va a poder leer.
/// </summary>
internal sealed partial class WhatsAppWebhookReader(
    IOptions<WhatsAppOptions> options,
    ILogger<WhatsAppWebhookReader> logger)
    : IWhatsAppWebhookReader
{
    private const string BusinessAccountObject = "whatsapp_business_account";
    private const string MessagesField = "messages";

    public WhatsAppWebhookBatch Read(ReadOnlyMemory<byte> body)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            // Sin la excepción: su mensaje puede citar un pedazo del cuerpo.
            LogUnreadable(logger, body.Length);

            return WhatsAppWebhookBatch.Empty;
        }

        using (document)
        {
            var reading = new Reading();
            Read(document.RootElement, reading);

            if (reading.IgnoredChanges > 0)
            {
                LogIgnoredChanges(logger, reading.IgnoredChanges);
            }

            if (reading.SkippedItems > 0)
            {
                LogSkippedItems(logger, reading.SkippedItems);
            }

            if (reading.WithoutSender > 0)
            {
                LogWithoutSender(logger, reading.WithoutSender);
            }

            return reading.Messages.Count == 0 && reading.Statuses.Count == 0
                ? WhatsAppWebhookBatch.Empty
                : new WhatsAppWebhookBatch(reading.Messages, reading.Statuses);
        }
    }

    private void Read(JsonElement root, Reading reading)
    {
        if (root.ValueKind is not JsonValueKind.Object
            || StringOf(root, "object") != BusinessAccountObject
            || !TryGetArray(root, "entry", out var entries))
        {
            reading.IgnoredChanges++;

            return;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!TryGetArray(entry, "changes", out var changes))
            {
                reading.IgnoredChanges++;

                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (change.ValueKind is not JsonValueKind.Object
                    || StringOf(change, "field") != MessagesField
                    || !TryGetProperty(change, "value", out var value)
                    || value.ValueKind is not JsonValueKind.Object
                    || !TryGetProperty(value, "metadata", out var metadata)
                    || metadata.ValueKind is not JsonValueKind.Object
                    || StringOf(metadata, "phone_number_id") != options.Value.PhoneNumberId)
                {
                    reading.IgnoredChanges++;

                    continue;
                }

                ReadMessages(value, reading);
                ReadStatuses(value, reading);
            }
        }
    }

    private static void ReadMessages(JsonElement value, Reading reading)
    {
        if (!TryGetArray(value, "messages", out var messages))
        {
            return;
        }

        var contacts = ReadContacts(value);

        foreach (var message in messages.EnumerateArray())
        {
            if (message.ValueKind is JsonValueKind.Object)
            {
                ReadMessage(message, contacts, reading);
            }
            else
            {
                reading.SkippedItems++;
            }
        }
    }

    private static void ReadMessage(JsonElement message, List<WhatsAppWebhookContact> contacts, Reading reading)
    {
        var waMessageId = StringOf(message, "id");

        if (!WhatsAppMessage.IsValidWaMessageId(waMessageId) || !TryReadTimestamp(message, out var occurredAtUtc))
        {
            reading.SkippedItems++;

            return;
        }

        // Sin un BSUID ni un número que se puedan leer no hay a quién asignarlo. Se cuenta aparte: si Meta cambiara la
        // forma de los identificadores, sería lo primero que se notaría, y cada uno es el mensaje de alguien.
        if (SenderOf(message, contacts) is not { } from)
        {
            reading.WithoutSender++;

            return;
        }

        var (kind, body, replyId) = ContentOf(message);

        reading.Messages.Add(new WhatsAppWebhookMessage(waMessageId!, from, kind, body, replyId, occurredAtUtc));
    }

    /// <summary>
    /// Quién lo mandó: <c>from_user_id</c> (el BSUID) y <c>from</c> (el número), completados con el contacto que coincide
    /// por alguno de los dos, que es el que trae el nombre. Un aviso de sistema no trae contactos.
    /// </summary>
    private static WhatsAppWebhookContact? SenderOf(JsonElement message, List<WhatsAppWebhookContact> contacts)
    {
        var userIdentifier = UserIdentifierOrNull(StringOf(message, "from_user_id"));
        var waId = WaIdOrNull(StringOf(message, "from"));

        var contact = contacts.FirstOrDefault(candidate =>
            (userIdentifier is not null && candidate.UserIdentifier == userIdentifier)
            || (waId is not null && candidate.WaId == waId));

        userIdentifier ??= contact?.UserIdentifier;
        waId ??= contact?.WaId;

        return userIdentifier is null && waId is null
            ? null
            : new WhatsAppWebhookContact(waId, userIdentifier, contact?.ProfileName);
    }

    private static (WhatsAppMessageKind Kind, string? Body, string? ReplyId) ContentOf(JsonElement message)
    {
        var type = StringOf(message, "type");
        var content = type is not null && TryGetProperty(message, type, out var element) && element.ValueKind is JsonValueKind.Object
            ? element
            : default;

        return type switch
        {
            "text" => (WhatsAppMessageKind.Text, StringOf(content, "body"), null),

            // Un botón de respuesta de un mensaje interactivo: su id y su título.
            "interactive" when StringOf(content, "type") == "button_reply"
                && TryGetProperty(content, "button_reply", out var reply) && reply.ValueKind is JsonValueKind.Object =>
                (WhatsAppMessageKind.ButtonReply, StringOf(reply, "title"), ReplyIdOrNull(StringOf(reply, "id"))),

            // El botón de respuesta rápida de una plantilla, como "Quiero entrar": su payload es el id.
            "button" => (WhatsAppMessageKind.ButtonReply, StringOf(content, "text"), ReplyIdOrNull(StringOf(content, "payload"))),

            "image" or "audio" or "video" or "document" or "sticker" => (WhatsAppMessageKind.Media, null, null),

            "system" => (WhatsAppMessageKind.System, StringOf(content, "body"), null),

            _ => (WhatsAppMessageKind.Other, null, null),
        };
    }

    private static List<WhatsAppWebhookContact> ReadContacts(JsonElement value)
    {
        var contacts = new List<WhatsAppWebhookContact>();

        if (!TryGetArray(value, "contacts", out var array))
        {
            return contacts;
        }

        foreach (var contact in array.EnumerateArray())
        {
            if (contact.ValueKind is not JsonValueKind.Object)
            {
                continue;
            }

            var profileName = TryGetProperty(contact, "profile", out var profile) && profile.ValueKind is JsonValueKind.Object
                ? StringOf(profile, "name")
                : null;

            contacts.Add(new WhatsAppWebhookContact(
                WaIdOrNull(StringOf(contact, "wa_id")), UserIdentifierOrNull(StringOf(contact, "user_id")), profileName));
        }

        return contacts;
    }

    private static void ReadStatuses(JsonElement value, Reading reading)
    {
        if (!TryGetArray(value, "statuses", out var statuses))
        {
            return;
        }

        foreach (var status in statuses.EnumerateArray())
        {
            var read = status.ValueKind is JsonValueKind.Object ? ReadStatus(status) : null;

            if (read is null)
            {
                reading.SkippedItems++;
            }
            else
            {
                reading.Statuses.Add(read);
            }
        }
    }

    /// <summary>
    /// "sent", "delivered", "read" y "failed". Los demás ("played", de los mensajes de voz, o uno nuevo) no cambian nada
    /// de lo que se guarda y se dejan afuera.
    /// </summary>
    private static WhatsAppWebhookStatus? ReadStatus(JsonElement status)
    {
        var waMessageId = StringOf(status, "id");
        WhatsAppMessageStatus? value = StringOf(status, "status") switch
        {
            "sent" => WhatsAppMessageStatus.Sent,
            "delivered" => WhatsAppMessageStatus.Delivered,
            "read" => WhatsAppMessageStatus.Read,
            "failed" => WhatsAppMessageStatus.Failed,
            _ => null,
        };

        if (!WhatsAppMessage.IsValidWaMessageId(waMessageId) || value is null || !TryReadTimestamp(status, out var occurredAtUtc))
        {
            return null;
        }

        return new WhatsAppWebhookStatus(waMessageId!, value.Value, occurredAtUtc, ErrorCodeOf(status));
    }

    private static int? ErrorCodeOf(JsonElement status)
    {
        if (!TryGetArray(status, "errors", out var errors))
        {
            return null;
        }

        foreach (var error in errors.EnumerateArray())
        {
            if (TryGetProperty(error, "code", out var code)
                && code.ValueKind is JsonValueKind.Number
                && code.TryGetInt32(out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>Meta manda la hora en segundos Unix, como texto.</summary>
    private static bool TryReadTimestamp(JsonElement element, out DateTime occurredAtUtc)
    {
        occurredAtUtc = default;

        if (!long.TryParse(StringOf(element, "timestamp"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            || seconds > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
        {
            return false;
        }

        occurredAtUtc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;

        return true;
    }

    // Un número que no es solo dígitos o un BSUID con espacios no se guardan: se leen como ausentes.
    private static string? WaIdOrNull(string? waId) => WhatsAppContact.IsValidWaId(waId) ? waId : null;

    private static string? UserIdentifierOrNull(string? userIdentifier) =>
        WhatsAppContact.IsValidUserIdentifier(userIdentifier) ? userIdentifier : null;

    // Un id de botón que no entra es un botón que no es nuestro: se guarda el mensaje, sin el id.
    private static string? ReplyIdOrNull(string? replyId) => WhatsAppMessage.IsValidReplyId(replyId) ? replyId : null;

    private static string? StringOf(JsonElement element, string property)
    {
        if (!TryGetProperty(element, property, out var value) || value.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            // JsonDocument acepta dentro de un texto bytes que no son UTF-8 válido y media letra escapada sin su pareja,
            // y recién GetString los rechaza. El campo se lee como ausente: un texto roto no frena el resto del webhook,
            // y un id roto deja afuera solo su ítem. Sin la excepción, que no agrega nada.
            return null;
        }
    }

    private static bool TryGetArray(JsonElement element, string property, out JsonElement array) =>
        TryGetProperty(element, property, out array) && array.ValueKind is JsonValueKind.Array;

    /// <summary>
    /// Por acá pasan todas las búsquedas de una propiedad de este lector, y no por
    /// <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/> directo. JsonDocument acepta un nombre de
    /// propiedad escapado con media letra sin su pareja (<c>"\udc00…"</c>), y TryGetProperty lo desescapa recién al
    /// compararlo con el que busca, y ahí tira. Cuando pasa, se buscan de a uno y el nombre roto se saltea, porque no es
    /// ninguno de los que se leen: así no frena el resto del objeto, ni del webhook. Fuera de un objeto, no hay nada.
    /// </summary>
    private static bool TryGetProperty(JsonElement element, string property, out JsonElement value)
    {
        value = default;

        if (element.ValueKind is not JsonValueKind.Object)
        {
            return false;
        }

        try
        {
            return element.TryGetProperty(property, out value);
        }
        catch (InvalidOperationException)
        {
            // Sin la excepción, que no agrega nada.
            return TryGetPropertySkippingBrokenNames(element, property, out value);
        }
    }

    private static bool TryGetPropertySkippingBrokenNames(JsonElement element, string property, out JsonElement value)
    {
        value = default;
        var found = false;

        // Sin cortar en el primero: con un nombre repetido gana el último, como en TryGetProperty.
        foreach (var candidate in element.EnumerateObject())
        {
            if (NameIs(candidate, property))
            {
                value = candidate.Value;
                found = true;
            }
        }

        return found;
    }

    private static bool NameIs(JsonProperty candidate, string name)
    {
        try
        {
            return candidate.NameEquals(name);
        }
        catch (InvalidOperationException)
        {
            // NameEquals también lo desescapa para comparar: el nombre roto no es ninguno de los que se buscan.
            return false;
        }
    }

    // Solo cuántos y cuánto: nada del cuerpo.
    [LoggerMessage(Level = LogLevel.Warning, Message = "A signed WhatsApp webhook of {Length} bytes is not valid JSON; it was ignored")]
    private static partial void LogUnreadable(ILogger logger, int length);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Ignored {Count} WhatsApp webhook changes that are not messages of the configured phone number")]
    private static partial void LogIgnoredChanges(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped {Count} WhatsApp webhook items that could not be read")]
    private static partial void LogSkippedItems(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Skipped {Count} WhatsApp webhook messages without a readable sender: no valid BSUID or phone number")]
    private static partial void LogWithoutSender(ILogger logger, int count);

    private sealed class Reading
    {
        public List<WhatsAppWebhookMessage> Messages { get; } = [];

        public List<WhatsAppWebhookStatus> Statuses { get; } = [];

        public int IgnoredChanges { get; set; }

        public int SkippedItems { get; set; }

        /// <summary>Los mensajes que se dejaron afuera porque no traían un BSUID ni un número que se pudieran leer.</summary>
        public int WithoutSender { get; set; }
    }
}
