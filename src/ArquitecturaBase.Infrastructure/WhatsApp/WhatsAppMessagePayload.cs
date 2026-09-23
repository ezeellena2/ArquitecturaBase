using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Abstractions.WhatsApp;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// El JSON de cada mensaje, con los nombres exactos de la Graph API (v25.0). Se arma con <see cref="JsonObject"/> para
/// que el código se lea igual que el ejemplo de la documentación de Meta. Los nombres de las plantillas salen de la
/// configuración: Application dice qué mandar y acá se decide con qué plantilla (sección 9 del spec).
/// </summary>
internal static class WhatsAppMessagePayload
{
    public static JsonObject Build(WhatsAppOutboundMessage message, WhatsAppTemplateOptions templates)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(templates);

        var payload = new JsonObject
        {
            ["messaging_product"] = "whatsapp",
            ["recipient_type"] = "individual",

            // Con el "+", como recomienda Meta: sin él, un número podría leerse como nacional.
            ["to"] = message.To.Value,
        };

        switch (message)
        {
            case WhatsAppTextMessage text:
                payload["type"] = "text";
                payload["text"] = new JsonObject { ["body"] = text.Body };
                break;

            case WhatsAppLinkButtonMessage link:
                payload["type"] = "interactive";
                payload["interactive"] = Interactive("cta_url", link.Body, link.Footer, new JsonObject
                {
                    ["name"] = "cta_url",
                    ["parameters"] = new JsonObject { ["display_text"] = link.ButtonText, ["url"] = link.Url },
                });
                break;

            case WhatsAppReplyButtonsMessage reply:
                payload["type"] = "interactive";
                payload["interactive"] = Interactive("button", reply.Body, reply.Footer, new JsonObject
                {
                    ["buttons"] = new JsonArray([.. reply.Buttons.Select(ReplyButton)]),
                });
                break;

            case WhatsAppLoginCodeMessage loginCode:
                payload["type"] = "template";
                payload["template"] = new JsonObject
                {
                    ["name"] = templates.LoginCode,
                    ["language"] = new JsonObject { ["code"] = loginCode.LanguageCode },

                    // Meta cambia el botón "Copiar código" a tipo url al crear la plantilla: el código va en el cuerpo
                    // y otra vez en el botón, o Meta rechaza el mensaje.
                    ["components"] = new JsonArray(
                        new JsonObject { ["type"] = "body", ["parameters"] = TextParameter(loginCode.Code) },
                        new JsonObject
                        {
                            ["type"] = "button",
                            ["sub_type"] = "url",
                            ["index"] = "0",
                            ["parameters"] = TextParameter(loginCode.Code),
                        }),
                };
                break;

            default:
                throw new ArgumentException($"There is no WhatsApp payload for {message.GetType().Name}.", nameof(message));
        }

        return payload;
    }

    private static JsonObject Interactive(string type, string body, string? footer, JsonObject action)
    {
        var interactive = new JsonObject
        {
            ["type"] = type,
            ["body"] = new JsonObject { ["text"] = body },
            ["action"] = action,
        };

        if (footer is not null)
        {
            interactive["footer"] = new JsonObject { ["text"] = footer };
        }

        return interactive;
    }

    private static JsonNode ReplyButton(WhatsAppReplyButton button) => new JsonObject
    {
        ["type"] = "reply",
        ["reply"] = new JsonObject { ["id"] = button.Id, ["title"] = button.Title },
    };

    private static JsonArray TextParameter(string text) => new(new JsonObject { ["type"] = "text", ["text"] = text });
}
