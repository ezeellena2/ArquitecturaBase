namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>Por qué Meta no aceptó un mensaje (sección 9 del spec). Cada motivo dice si vale la pena reintentar.</summary>
internal enum WhatsAppSendFailure
{
    /// <summary>131030: el destinatario no está en la lista del número de prueba. Pasa solo con ese número.</summary>
    RecipientNotAllowed,

    /// <summary>131047: pasaron más de 24 horas desde el último mensaje de la persona; solo se puede mandar una plantilla.</summary>
    OutsideCustomerServiceWindow,

    /// <summary>131026: no se pudo entregar (el número no tiene WhatsApp, o su versión es vieja).</summary>
    Undeliverable,

    /// <summary>131056: demasiados mensajes seguidos a la misma persona. Se reintenta a los 6 segundos o más.</summary>
    PairRateLimited,

    /// <summary>130429: se llegó al límite de mensajes de la cuenta. Se reintenta.</summary>
    RateLimited,

    /// <summary>
    /// 0 o 190, o un 401 sin un código conocido: el token dejó de valer. Va al log como error y a la salud de la app.
    /// Se arregla cargando otro en <c>WhatsApp:AccessToken</c> y reiniciando la Api, que lee las opciones al arrancar.
    /// </summary>
    InvalidToken,

    /// <summary>
    /// 3 (que Meta manda con un 500), 10 o de 200 a 299, o un 403 sin un código conocido: al token le falta un permiso
    /// (<c>whatsapp_business_messaging</c>) o el usuario del sistema no tiene asignada la cuenta de WhatsApp. Va al log
    /// como error y a la salud de la app. Se arregla dándole los permisos (un permiso nuevo pide generar otro token) y
    /// reiniciando la Api, que lee las opciones al arrancar.
    /// </summary>
    MissingPermission,

    /// <summary>Un 5xx, un timeout o la red: algo pasajero. Se reintenta.</summary>
    Transient,

    /// <summary>Cualquier otro error de Meta, con su código si vino. Reintentar daría lo mismo.</summary>
    Other,
}

internal static class WhatsAppSendFailureExtensions
{
    public static bool IsRetryable(this WhatsAppSendFailure failure) =>
        failure is WhatsAppSendFailure.PairRateLimited or WhatsAppSendFailure.RateLimited or WhatsAppSendFailure.Transient;

    /// <summary>
    /// Un problema de configuración del token, no del mensaje: todos los envíos van a fallar igual hasta que alguien lo
    /// arregle en Meta y reinicie la Api.
    /// </summary>
    public static bool IsTokenProblem(this WhatsAppSendFailure failure) =>
        failure is WhatsAppSendFailure.InvalidToken or WhatsAppSendFailure.MissingPermission;
}

/// <summary>
/// Cómo terminó un envío: con el id que le dio Meta (<c>messages[0].id</c>, que se guarda para cruzarlo con los estados)
/// o con el motivo del rechazo y el código de Meta, si vino. Los errores de Meta son un resultado, no una excepción.
/// </summary>
internal sealed record WhatsAppSendResult
{
    private WhatsAppSendResult(string? waMessageId, WhatsAppSendFailure? failure, int? metaErrorCode)
    {
        WaMessageId = waMessageId;
        Failure = failure;
        MetaErrorCode = metaErrorCode;
    }

    public string? WaMessageId { get; }

    public WhatsAppSendFailure? Failure { get; }

    public int? MetaErrorCode { get; }

    public bool IsSent => Failure is null;

    public static WhatsAppSendResult Sent(string waMessageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(waMessageId);

        return new(waMessageId, failure: null, metaErrorCode: null);
    }

    public static WhatsAppSendResult Failed(WhatsAppSendFailure failure, int? metaErrorCode = null) =>
        new(waMessageId: null, failure, metaErrorCode);
}
