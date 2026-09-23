using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// La sección <c>WhatsApp</c> (sección 14 del spec), con lo que usa el envío. El interruptor es
/// <see cref="PhoneNumberId"/>: sin él, WhatsApp queda apagado y estas opciones ni se registran. El token es secreto:
/// en desarrollo va en user-secrets y en producción, en variables de entorno o un almacén de secretos. Se leen una vez,
/// al arrancar (<c>IOptions</c>): cambiar un valor, el token incluido, pide reiniciar la Api.
/// </summary>
internal sealed class WhatsAppOptions
{
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// Meta no acepta más de un mensaje cada 6 segundos a la misma persona (error 131056): reintentar antes es perder el
    /// intento. Es el mínimo de <see cref="RetryDelaySeconds"/>.
    /// </summary>
    public const int MinRetryDelaySeconds = 6;

    /// <summary>El id del número que manda los mensajes, el de la Graph API (no el número de teléfono).</summary>
    public string? PhoneNumberId { get; init; }

    /// <summary>
    /// El token del usuario del sistema. No vence; si se filtra, se revoca en Meta y se genera otro. Se lee al arrancar:
    /// uno nuevo, o uno con más permisos, recién se usa después de reiniciar la Api.
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>La versión de la Graph API, con el formato de Meta: "v25.0".</summary>
    public string GraphApiVersion { get; init; } = "v25.0";

    public WhatsAppTemplateOptions Templates { get; init; } = new();

    /// <summary>
    /// Espera antes de reintentar lo que Meta rechazó por un rato (131056, 130429, un 5xx o un timeout). Por defecto y
    /// como mínimo, <see cref="MinRetryDelaySeconds"/>: así ningún reintento choca con el límite por persona.
    /// </summary>
    [Range(
        MinRetryDelaySeconds,
        300,
        ErrorMessage = "WhatsApp:RetryDelaySeconds must be between {1} and {2}: Meta rejects a second message to the same person within 6 seconds (error 131056).")]
    public int RetryDelaySeconds { get; init; } = MinRetryDelaySeconds;

    /// <summary>Mensajes que puede tener la cola antes de rechazar los nuevos, como EmailQueue.</summary>
    [Range(1, 10_000)]
    public int QueueCapacity { get; init; } = 100;
}

/// <summary>
/// Los nombres de las plantillas aprobadas en Meta. Cada una existe en "es" y "en". Los usa WhatsAppMessagePayload: los
/// mensajes de Application dicen qué mandar y en qué idioma, nunca con qué plantilla.
/// </summary>
internal sealed class WhatsAppTemplateOptions
{
    /// <summary>La plantilla de autenticación con el código de ingreso.</summary>
    public string LoginCode { get; init; } = "codigo_ingreso";
}
