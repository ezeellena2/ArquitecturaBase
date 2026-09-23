using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Webhooks como los que manda Meta, con la forma de su documentación (referencia del campo <c>messages</c> y de los
/// BSUID): <c>metadata.phone_number_id</c>, los contactos con su <c>wa_id</c>, su nombre de perfil y su
/// <c>user_id</c>, los mensajes con <c>from</c> y <c>from_user_id</c>, y los estados con sus errores.
/// </summary>
internal static class MetaWebhook
{
    /// <summary>La cuenta de WhatsApp de los webhooks de prueba: Meta la manda en <c>entry.id</c> y no se usa.</summary>
    public const string BusinessAccountId = "100000000000009";

    public const string DisplayPhoneNumber = "15551632662";

    /// <summary>Un webhook con los mensajes y los estados dados, del número de la Api de los tests o de otro.</summary>
    public static byte[] Build(
        IEnumerable<JsonObject>? contacts = null,
        IEnumerable<JsonObject>? messages = null,
        IEnumerable<JsonObject>? statuses = null,
        string phoneNumberId = ApiFactory.WhatsAppPhoneNumberId,
        string objectName = "whatsapp_business_account",
        string field = "messages")
    {
        var value = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["metadata"] = new JsonObject
            {
                ["display_phone_number"] = DisplayPhoneNumber,
                ["phone_number_id"] = phoneNumberId,
            },
        };

        if (contacts is not null)
        {
            value["contacts"] = new JsonArray([.. contacts]);
        }

        if (messages is not null)
        {
            value["messages"] = new JsonArray([.. messages]);
        }

        if (statuses is not null)
        {
            value["statuses"] = new JsonArray([.. statuses]);
        }

        var root = new JsonObject
        {
            ["object"] = objectName,
            ["entry"] = new JsonArray(new JsonObject
            {
                ["id"] = BusinessAccountId,
                ["changes"] = new JsonArray(new JsonObject { ["value"] = value, ["field"] = field }),
            }),
        };

        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    /// <summary>Una persona que le escribe al bot: su BSUID, su número y su nombre de perfil.</summary>
    public static JsonObject Contact(string? waId, string? userIdentifier, string? profileName)
    {
        var contact = new JsonObject
        {
            ["profile"] = new JsonObject { ["name"] = profileName },
            ["wa_id"] = waId ?? string.Empty,
        };

        if (userIdentifier is not null)
        {
            contact["user_id"] = userIdentifier;
        }

        return contact;
    }

    public static JsonObject Text(string waMessageId, string? from, string? fromUserIdentifier, DateTime sentAtUtc, string body) =>
        Message(waMessageId, from, fromUserIdentifier, sentAtUtc, "text", new JsonObject { ["body"] = body });

    /// <summary>La persona tocó un botón de un mensaje interactivo (<c>interactive.button_reply</c>).</summary>
    public static JsonObject ButtonReply(string waMessageId, string? from, string? fromUserIdentifier, DateTime sentAtUtc, string id, string title)
    {
        var message = Message(waMessageId, from, fromUserIdentifier, sentAtUtc, "interactive", new JsonObject
        {
            ["type"] = "button_reply",
            ["button_reply"] = new JsonObject { ["id"] = id, ["title"] = title },
        });
        message["context"] = new JsonObject { ["from"] = DisplayPhoneNumber, ["id"] = "wamid.context" };

        return message;
    }

    /// <summary>La persona tocó el botón de respuesta rápida de una plantilla (<c>button</c>, con su payload).</summary>
    public static JsonObject TemplateButton(string waMessageId, string? from, string? fromUserIdentifier, DateTime sentAtUtc, string payload, string text) =>
        Message(waMessageId, from, fromUserIdentifier, sentAtUtc, "button", new JsonObject { ["payload"] = payload, ["text"] = text });

    public static JsonObject Image(string waMessageId, string? from, string? fromUserIdentifier, DateTime sentAtUtc) =>
        Message(waMessageId, from, fromUserIdentifier, sentAtUtc, "image", new JsonObject
        {
            ["caption"] = "Mirá esta foto",
            ["mime_type"] = "image/jpeg",
            ["sha256"] = "JTF3Dq2S5uMyJnl2OZw6a0VvbQuVWb0uYUCb+KeBP4U=",
            ["id"] = "1003383421387256",
        });

    /// <summary>El aviso de cambio de número. Según la documentación de Meta, no trae el arreglo de contactos.</summary>
    public static JsonObject NumberChanged(string waMessageId, string from, DateTime sentAtUtc, string newWaId) =>
        Message(waMessageId, from, fromUserIdentifier: null, sentAtUtc, "system", new JsonObject
        {
            ["body"] = $"User Ana changed from {from} to {newWaId}",
            ["wa_id"] = newWaId,
            ["type"] = "user_changed_number",
        });

    public static JsonObject Reaction(string waMessageId, string? from, string? fromUserIdentifier, DateTime sentAtUtc) =>
        Message(waMessageId, from, fromUserIdentifier, sentAtUtc, "reaction", new JsonObject
        {
            ["message_id"] = "wamid.reacted",
            ["emoji"] = "👍",
        });

    public static JsonObject Status(string waMessageId, string status, DateTime atUtc, string? recipientWaId = null, int? errorCode = null)
    {
        var json = new JsonObject
        {
            ["id"] = waMessageId,
            ["status"] = status,
            ["timestamp"] = Timestamp(atUtc),
            ["recipient_id"] = recipientWaId ?? "5493415550000",
            ["pricing"] = new JsonObject
            {
                ["billable"] = true,
                ["pricing_model"] = "PMP",
                ["type"] = "free_customer_service",
                ["category"] = "service",
            },
        };

        if (errorCode is not null)
        {
            json["errors"] = new JsonArray(new JsonObject
            {
                ["code"] = errorCode,
                ["title"] = "Message undeliverable.",
                ["message"] = "Message undeliverable.",
                ["error_data"] = new JsonObject { ["details"] = "Message could not be delivered." },
            });
        }

        return json;
    }

    /// <summary>La firma que manda Meta: <c>sha256=</c> y el HMAC-SHA256 del cuerpo, con el secreto de la app, en hex.</summary>
    public static string Sign(byte[] body, string appSecret = ApiFactory.WhatsAppAppSecret) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));

    /// <summary>Meta manda las horas en segundos Unix, como texto.</summary>
    public static string Timestamp(DateTime atUtc) =>
        new DateTimeOffset(atUtc, TimeSpan.Zero).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>La hora tal como vuelve de Meta: sin fracciones de segundo.</summary>
    public static DateTime TruncatedToSeconds(DateTime atUtc) =>
        new(atUtc.Ticks - (atUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    public static string UniqueWaMessageId() =>
        "wamid.HBgNNTQ5MzQx" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture).ToUpperInvariant() + "AA==";

    /// <summary>Un BSUID con la forma de los argentinos: "AR." y 16 dígitos.</summary>
    public static string UniqueBsuid() =>
        "AR." + Random.Shared.NextInt64(1_000_000_000_000_000, 10_000_000_000_000_000).ToString(CultureInfo.InvariantCulture);

    /// <summary>Un <c>wa_id</c> distinto por test: un celular de Córdoba con el 9, sin el "+", como lo manda WhatsApp.</summary>
    public static string UniqueWaId() => TestPhones.Unique().Value[1..];

    private static JsonObject Message(
        string waMessageId,
        string? from,
        string? fromUserIdentifier,
        DateTime sentAtUtc,
        string type,
        JsonObject content)
    {
        var message = new JsonObject
        {
            ["from"] = from ?? string.Empty,
            ["id"] = waMessageId,
            ["timestamp"] = Timestamp(sentAtUtc),
            ["type"] = type,
            [type] = content,
        };

        if (fromUserIdentifier is not null)
        {
            message["from_user_id"] = fromUserIdentifier;
        }

        return message;
    }
}
