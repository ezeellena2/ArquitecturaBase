using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

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
    private const int MaxFlowResponseDepth = 4;
    private const int MaxFlowResponseFields = 50;
    // A block answers each of its questions once per row, up to ten, besides «¿Agregás otro ítem?».
    private const int MaxFlowRowResponseFields = 450;

    // Stage 2 of the assistant authoring: what makes a document a spreadsheet, by its name or by the type Meta sends.
    private static readonly string[] SpreadsheetExtensions = [".xlsx", ".xlsm", ".xlsb", ".xls", ".csv"];
    private static readonly string[] SpreadsheetMediaTypes =
    [
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.ms-excel",
        "application/vnd.ms-excel.sheet.macroenabled.12", "application/vnd.ms-excel.sheet.binary.macroenabled.12", "text/csv",
    ];

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

        if (IsFlowReplyMessage(message))
        {
            if (TryReadFlowResponse(message) is not { } responseJson)
            {
                reading.SkippedItems++;
                return;
            }

            reading.Messages.Add(new WhatsAppWebhookMessage(
                waMessageId!, from, WhatsAppMessageKind.FlowReply, null, null, occurredAtUtc, responseJson));
            return;
        }

        if (StringOf(message, "type") == "audio")
        {
            reading.Messages.Add(new WhatsAppWebhookMessage(waMessageId!, from, WhatsAppMessageKind.Audio, null, null, occurredAtUtc)
                { Audio = ReadAudio(message) });
            return;
        }

        if (StringOf(message, "type") == "document" && ReadSpreadsheet(message) is { } spreadsheet)
        {
            reading.Messages.Add(new WhatsAppWebhookMessage(waMessageId!, from, WhatsAppMessageKind.Spreadsheet, spreadsheet.Caption, null, occurredAtUtc)
                { Spreadsheet = spreadsheet.Descriptor });
            return;
        }

        // Task 8 of the documents: a photo (JPEG or PNG) or any other document is a received document, with its caption and its descriptor.
        if (ReadDocument(message) is { } received)
        {
            reading.Messages.Add(new WhatsAppWebhookMessage(waMessageId!, from, WhatsAppMessageKind.ReceivedDocument, received.Caption, null, occurredAtUtc)
                { Document = received.Descriptor });
            return;
        }

        // W1-A: a location keeps its point and, since W3, the address of a chosen place; its name is not kept. A point Meta sends out of
        // range stays a location without one.
        if (StringOf(message, "type") == "location")
        {
            reading.Messages.Add(new WhatsAppWebhookMessage(waMessageId!, from, WhatsAppMessageKind.Location, null, null, occurredAtUtc)
                { Location = ReadLocation(message) });
            return;
        }

        // W3: a contact keeps the formatted name and the phones of its first card; without a card it stays a contact without one.
        if (StringOf(message, "type") == "contacts")
        {
            reading.Messages.Add(new WhatsAppWebhookMessage(waMessageId!, from, WhatsAppMessageKind.Contact, null, null, occurredAtUtc)
                { SharedContact = ReadSharedContact(message) });
            return;
        }

        var (kind, body, replyId, flowResponseJson) = ContentOf(message);

        reading.Messages.Add(new WhatsAppWebhookMessage(waMessageId!, from, kind, body, replyId, occurredAtUtc, flowResponseJson));
    }

    private static WhatsAppWebhookLocation? ReadLocation(JsonElement message)
    {
        if (!TryGetProperty(message, "location", out var location) || location.ValueKind != JsonValueKind.Object
            || !TryGetProperty(location, "latitude", out var latitude) || !TryGetProperty(location, "longitude", out var longitude)
            || latitude.ValueKind != JsonValueKind.Number || longitude.ValueKind != JsonValueKind.Number
            || !latitude.TryGetDecimal(out var lat) || !longitude.TryGetDecimal(out var lng)) return null;
        lat = decimal.Round(lat, 6, MidpointRounding.AwayFromZero);
        lng = decimal.Round(lng, 6, MidpointRounding.AwayFromZero);
        return lat is >= -90 and <= 90 && lng is >= -180 and <= 180 ? new(lat, lng, StringOf(location, "address")) : null;
    }

    /// <summary>
    /// W3: the formatted name and the phones of the first card of a shared contact (<c>contacts[0].name.formatted_name</c> and each
    /// <c>phones[].phone</c>), as Meta sends them; the message decides what it keeps. Null when the first card is not an object.
    /// </summary>
    private static WhatsAppWebhookSharedContact? ReadSharedContact(JsonElement message)
    {
        if (!TryGetArray(message, "contacts", out var cards) || cards.GetArrayLength() == 0 || cards[0] is not { ValueKind: JsonValueKind.Object } card)
            return null;
        var name = TryGetProperty(card, "name", out var names) ? StringOf(names, "formatted_name") : null;
        string[] phones = TryGetArray(card, "phones", out var entries)
            ? [.. entries.EnumerateArray().Select(entry => StringOf(entry, "phone")).OfType<string>()]
            : [];
        return new(name, phones);
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

    private static (WhatsAppMessageKind Kind, string? Body, string? ReplyId, string? FlowResponseJson) ContentOf(JsonElement message)
    {
        var type = StringOf(message, "type");
        var content = type is not null && TryGetProperty(message, type, out var element) && element.ValueKind is JsonValueKind.Object
            ? element
            : default;

        return type switch
        {
            "text" => (WhatsAppMessageKind.Text, StringOf(content, "body"), null, null),

            // Un botón de respuesta de un mensaje interactivo: su id y su título.
            "interactive" when StringOf(content, "type") == "button_reply"
                && TryGetProperty(content, "button_reply", out var reply) && reply.ValueKind is JsonValueKind.Object =>
                (WhatsAppMessageKind.ButtonReply, StringOf(reply, "title"), ReplyIdOrNull(StringOf(reply, "id")), null),

            // El botón de respuesta rápida de una plantilla, como "Quiero entrar": su payload es el id.
            "button" => (WhatsAppMessageKind.ButtonReply, StringOf(content, "text"), ReplyIdOrNull(StringOf(content, "payload")), null),

            "image" or "audio" or "video" or "document" or "sticker" => (WhatsAppMessageKind.Media, null, null, null),

            "system" => (WhatsAppMessageKind.System, StringOf(content, "body"), null, null),

            _ => (WhatsAppMessageKind.Other, null, null, null),
        };
    }

    private static WhatsAppWebhookAudioDescriptor? ReadAudio(JsonElement message)
    {
        if (!TryGetProperty(message, "audio", out var audio) || StringOf(audio, "id") is not { Length: > 0 and <= 128 } id ||
            !id.All(char.IsAsciiDigit) || StringOf(audio, "mime_type") is not { Length: > 0 and <= 128 } type ||
            !MediaTypeHeaderValue.TryParse(type, out var mime) || !string.Equals(mime.MediaType, "audio/ogg", StringComparison.OrdinalIgnoreCase) ||
            mime.Parameters.Any(p => !p.Name.Equals("codecs", StringComparison.OrdinalIgnoreCase) || !string.Equals(p.Value?.Trim('"'), "opus", StringComparison.OrdinalIgnoreCase)) ||
            HexHashOrNull(StringOf(audio, "sha256")) is not { } hash) return null;
        return new(id, type, hash);
    }

    /// <summary>
    /// A document that is a spreadsheet, by its name or by the type Meta sends, with its caption and what Meta says of its file. A CSV
    /// may come as text/csv, text/plain or application/vnd.ms-excel, so text/plain counts only with a .csv name; the bytes decide later
    /// what it is. The message decides whether the descriptor can be downloaded. Null for any other document, which stays as media.
    /// </summary>
    private static (string? Caption, WhatsAppWebhookSpreadsheetDescriptor Descriptor)? ReadSpreadsheet(JsonElement message)
    {
        if (!TryGetProperty(message, "document", out var document) || document.ValueKind is not JsonValueKind.Object) return null;
        var fileName = StringOf(document, "filename");
        var type = StringOf(document, "mime_type");
        if (!IsSpreadsheet(fileName, type)) return null;
        return (StringOf(document, "caption"),
            new WhatsAppWebhookSpreadsheetDescriptor(StringOf(document, "id"), type, HexHashOrNull(StringOf(document, "sha256")), fileName));
    }

    /// <summary>
    /// A photo Meta declares as a JPEG or a PNG, or a document that is not a spreadsheet (a PDF, a Word or any other file), with its caption
    /// and what Meta says of its file; a photo has no name. The bytes decide later what it is, and the message whether the descriptor can be
    /// downloaded. Null for anything else: a video, a sticker or a photo of another type stay as media.
    /// </summary>
    private static (string? Caption, WhatsAppWebhookDocumentDescriptor Descriptor)? ReadDocument(JsonElement message)
    {
        var type = StringOf(message, "type");
        if (type is not ("image" or "document") || !TryGetProperty(message, type, out var media) || media.ValueKind is not JsonValueKind.Object) return null;
        var mimeType = StringOf(media, "mime_type");
        if (type == "image" && !(MediaTypeHeaderValue.TryParse(mimeType, out var mime) && mime.MediaType is { } mediaType
                && (mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) || mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase))))
            return null;
        return (StringOf(media, "caption"), new WhatsAppWebhookDocumentDescriptor(StringOf(media, "id"), mimeType, HexHashOrNull(StringOf(media, "sha256")),
            type == "document" ? StringOf(media, "filename") : null));
    }

    private static bool IsSpreadsheet(string? fileName, string? type) =>
        fileName is not null && SpreadsheetExtensions.Contains(Path.GetExtension(fileName.Trim()), StringComparer.OrdinalIgnoreCase)
        || MediaTypeHeaderValue.TryParse(type, out var mime) && mime.MediaType is { } mediaType
            && SpreadsheetMediaTypes.Contains(mediaType, StringComparer.OrdinalIgnoreCase);

    // Meta sends a media SHA-256 in base64 (or hex): it is kept in lowercase hex, and anything else is not a hash.
    private static string? HexHashOrNull(string? hash)
    {
        if (hash is not { Length: > 0 and <= 128 }) return null;
        try
        {
            var bytes = hash.Length == 64 ? Convert.FromHexString(hash) : Convert.FromBase64String(hash);
            return bytes.Length == 32 ? Convert.ToHexStringLower(bytes) : null;
        }
        catch (FormatException) { return null; }
    }

    private static bool IsFlowReplyMessage(JsonElement message) =>
        StringOf(message, "type") == "interactive"
        && TryGetProperty(message, "interactive", out var interactive)
        && StringOf(interactive, "type") == "nfm_reply";

    private static string? TryReadFlowResponse(JsonElement message)
    {
        if (!TryGetProperty(message, "interactive", out var interactive)
            || !TryGetProperty(interactive, "nfm_reply", out var reply)
            || reply.ValueKind is not JsonValueKind.Object
            || StringOf(reply, "name") != "flow"
            || StringOf(reply, "response_json") is not { } responseJson)
        {
            return null;
        }

        if (Encoding.UTF8.GetByteCount(responseJson) > (WhatsAppFlowResponseLimits.HasFileLists(responseJson) || WhatsAppFlowResponseLimits.HasRows(responseJson)
                ? WhatsAppFlowProfile.MaxMediaResponseJsonBytes : WhatsAppFlowProfile.MaxResponseJsonBytes))
        {
            return OversizedResponseMarker(responseJson);
        }

        return IsBoundedFlowResponse(responseJson) ? responseJson : null;
    }

    private static string OversizedResponseMarker(string responseJson)
    {
        string? token = null;
        try
        {
            using var document = JsonDocument.Parse(responseJson, new JsonDocumentOptions { MaxDepth = MaxFlowResponseDepth });
            var candidate = StringOf(document.RootElement, "flow_token");
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length <= 128) token = candidate;
        }
        catch (JsonException)
        {
            // A malformed oversized response is still retained as a redacted rejection event.
        }

        var marker = new Dictionary<string, string>(StringComparer.Ordinal);
        if (token is not null) marker["flow_token"] = token;
        marker[WhatsAppFlowProfile.ResponseErrorProperty] = WhatsAppFlowProfile.ResponseTooLargeError;
        return JsonSerializer.Serialize(marker);
    }

    private static bool IsBoundedFlowResponse(string responseJson)
    {
        try
        {
            using var document = JsonDocument.Parse(responseJson, new JsonDocumentOptions { MaxDepth = MaxFlowResponseDepth });
            var root = document.RootElement;
            if (root.ValueKind is not JsonValueKind.Object)
            {
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var hasToken = false;
            var fields = 0;
            var rows = false;

            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Length is 0 or > 64 || !names.Add(property.Name))
                {
                    return false;
                }

                if (property.Name == "flow_token")
                {
                    var token = property.Value.ValueKind is JsonValueKind.String ? property.Value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
                    {
                        return false;
                    }

                    hasToken = true;
                    continue;
                }

                if (!WhatsAppFlowResponseLimits.IsFieldKey(property.Name, out var row)
                    || property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Array))
                {
                    return false;
                }

                rows |= row.HasValue;
                fields++;
                if (fields > MaxFlowRowResponseFields)
                {
                    return false;
                }
            }

            return hasToken && fields <= (rows ? MaxFlowRowResponseFields : MaxFlowResponseFields);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
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
