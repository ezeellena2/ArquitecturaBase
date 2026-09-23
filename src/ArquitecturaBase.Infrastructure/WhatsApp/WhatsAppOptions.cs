using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// La sección <c>WhatsApp</c> (sección 14 del spec), con lo que usan el envío y el webhook. El interruptor es
/// <see cref="PhoneNumberId"/>: sin él, WhatsApp queda apagado y estas opciones ni se registran. El token y los dos
/// secretos del webhook son secretos: en desarrollo van en user-secrets y en producción, en variables de entorno o un
/// almacén de secretos. Se leen una vez, al arrancar (<c>IOptions</c>): cambiar un valor, el token incluido, pide
/// reiniciar la Api.
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

    /// <summary>
    /// El secreto de la app de Meta, con el que Meta firma los webhooks (<c>X-Hub-Signature-256</c>). Junto con
    /// <see cref="VerifyToken"/> prende el webhook: sin ninguno de los dos, el envío funciona igual y el webhook queda
    /// apagado; con uno solo, la Api no arranca.
    /// </summary>
    public string? AppSecret { get; init; }

    /// <summary>
    /// La palabra de verificación del webhook, la misma que se carga en Meta al configurarlo: una larga y al azar,
    /// inventada para esto. Es secreta, como <see cref="AppSecret"/>.
    /// </summary>
    public string? VerifyToken { get; init; }

    /// <summary>Si están los dos secretos del webhook. Uno solo es un error que frena el arranque.</summary>
    public bool HasWebhookSecrets => !string.IsNullOrWhiteSpace(AppSecret) && !string.IsNullOrWhiteSpace(VerifyToken);

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

    /// <summary>
    /// Cada cuánto revisa el procesador los mensajes entrantes pendientes, además de despertarse con cada webhook
    /// (sección 7 del spec). La revisión es lo que encuentra lo que quedó pendiente de antes de un reinicio, o lo que
    /// recibió otra instancia.
    /// </summary>
    [Range(1, 3600, ErrorMessage = "WhatsApp:InboundPollSeconds must be between {1} and {2}.")]
    public int InboundPollSeconds { get; init; } = 30;

    /// <summary>
    /// Si el procesador de los mensajes entrantes corre solo, en segundo plano. Apagado, los mensajes esperan a que
    /// alguien llame a <c>WhatsAppInboundProcessor.ProcessPendingAsync</c>: lo apagan los tests de integración, que lo
    /// llaman cuando quieren y así saben qué respondió el bot a qué.
    /// </summary>
    public bool ProcessInboundInBackground { get; init; } = true;

    /// <summary>
    /// Solo para el número de prueba de Meta (spec 16): su lista de destinatarios guarda los celulares argentinos sin
    /// el 9 y rechaza el envío a "+549…" con el error 131030. Prendido, el "to" de los celulares argentinos va sin el 9;
    /// el número sigue guardado con el 9 en todo lo demás. En producción queda apagado, porque no hay lista.
    /// </summary>
    public bool SendArgentineMobilesWithoutNine { get; init; }
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
