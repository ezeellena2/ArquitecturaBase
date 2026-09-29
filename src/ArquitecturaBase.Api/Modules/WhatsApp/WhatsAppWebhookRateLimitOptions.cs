using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Modules.WhatsApp;

/// <summary>El límite por IP del webhook de WhatsApp, en la sección RateLimiting, con las mismas claves de siempre.</summary>
internal sealed class WhatsAppWebhookRateLimitOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>
    /// Webhooks de WhatsApp: 600 por minuto. Meta manda desde varias direcciones y agrupa las novedades, así que es
    /// mucho más de lo que llega; un 429 no pierde nada, porque Meta reintenta.
    /// </summary>
    [Range(1, 100_000)]
    public int WhatsAppWebhookPermitLimit { get; init; } = 600;

    [Range(1, 1440)]
    public int WhatsAppWebhookWindowMinutes { get; init; } = 1;
}
