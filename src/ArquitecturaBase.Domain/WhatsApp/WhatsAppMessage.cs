using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.WhatsApp;

/// <summary>
/// Un mensaje de WhatsApp, entrante o saliente (sección 6.5 del spec del ingreso con WhatsApp). El
/// <see cref="WaMessageId"/> es único y es la defensa contra los duplicados: Meta reintenta durante días, y un mensaje
/// que ya está guardado no se vuelve a guardar. No se guarda el payload crudo, y un saliente con un código o un enlace
/// se guarda con su resumen seguro ("[código]"), nunca con el código.
/// </summary>
public sealed class WhatsAppMessage : AggregateRoot
{
    /// <summary>Un <c>wamid</c> tiene unos 60 caracteres; el resto es margen.</summary>
    public const int MaxWaMessageIdLength = 256;

    /// <summary>El largo máximo de un texto de WhatsApp.</summary>
    public const int MaxBodyLength = 4096;

    /// <summary>El largo máximo del identificador de un botón de respuesta.</summary>
    public const int MaxReplyIdLength = 256;

    // Para EF Core.
    private WhatsAppMessage()
    {
        WaMessageId = string.Empty;
    }

    private WhatsAppMessage(
        Guid? contactId,
        WhatsAppMessageDirection direction,
        string waMessageId,
        WhatsAppMessageKind kind,
        string? body,
        string? replyId,
        DateTime occurredAtUtc)
    {
        ContactId = contactId;
        Direction = direction;
        WaMessageId = waMessageId;
        Kind = kind;
        Body = body;
        ReplyId = replyId;
        OccurredAtUtc = occurredAtUtc;
    }

    /// <summary>
    /// Quién lo mandó o a quién se le mandó. Un entrante siempre tiene contacto; un saliente puede no tenerlo, como un
    /// código de ingreso mandado a un número que nunca le escribió al bot.
    /// </summary>
    public Guid? ContactId { get; private set; }

    public WhatsAppMessageDirection Direction { get; private set; }

    /// <summary>El id de Meta (<c>wamid.…</c>). Único.</summary>
    public string WaMessageId { get; private set; }

    public WhatsAppMessageKind Kind { get; private set; }

    /// <summary>
    /// El texto, el título del botón o el aviso de WhatsApp; null en una foto o un audio. En un saliente, su resumen
    /// seguro. Se recorta a <see cref="MaxBodyLength"/>. También es null en cualquier mensaje viejo: la retención (90 días
    /// por defecto) borra el texto y deja el resto de la fila, así que nada puede contar con que el texto siga ahí.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>
    /// El identificador del botón que se tocó: el id de un botón de respuesta o el payload de un botón de plantilla. Es
    /// con lo que el bot sabe qué eligió la persona.
    /// </summary>
    public string? ReplyId { get; private set; }

    /// <summary>Cuándo se mandó, con la hora de Meta en un entrante y la nuestra en un saliente.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    /// <summary>
    /// Cuándo lo procesó el bot. Null en un entrante pendiente, y siempre en un saliente: los pendientes tienen un
    /// índice propio, así que marcar uno lo saca de la lista que revisa el procesador.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; private set; }

    /// <summary>El último estado que avisó Meta de un saliente; null si todavía no avisó ninguno.</summary>
    public WhatsAppMessageStatus? Status { get; private set; }

    /// <summary>Cuándo pasó <see cref="Status"/>, con la hora de Meta.</summary>
    public DateTime? StatusAtUtc { get; private set; }

    /// <summary>El código de error de Meta de un saliente que falló (por ejemplo, 131026).</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>Un mensaje que mandó la persona. Queda pendiente para el bot.</summary>
    public static WhatsAppMessage Inbound(
        Guid contactId,
        string waMessageId,
        WhatsAppMessageKind kind,
        string? body,
        string? replyId,
        DateTime occurredAtUtc)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("An inbound WhatsApp message needs its contact.", nameof(contactId));
        }

        return new WhatsAppMessage(
            contactId,
            WhatsAppMessageDirection.Inbound,
            RequireId(waMessageId, MaxWaMessageIdLength, nameof(waMessageId)),
            kind,
            Truncate(body),
            OptionalId(replyId, MaxReplyIdLength, nameof(replyId)),
            occurredAtUtc);
    }

    /// <summary>
    /// Un mensaje que Meta aceptó mandar, con el id que devolvió. <paramref name="body"/> es su resumen seguro: nunca
    /// el código ni la URL del enlace. Hasta que Meta avise algo, no tiene estado.
    /// </summary>
    public static WhatsAppMessage Outbound(
        Guid? contactId,
        string waMessageId,
        WhatsAppMessageKind kind,
        string body,
        DateTime sentAtUtc)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("The contact id cannot be empty: pass null when there is no contact.", nameof(contactId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new WhatsAppMessage(
            contactId,
            WhatsAppMessageDirection.Outbound,
            RequireId(waMessageId, MaxWaMessageIdLength, nameof(waMessageId)),
            kind,
            Truncate(body),
            replyId: null,
            sentAtUtc);
    }

    public static bool IsValidWaMessageId(string? waMessageId) => IsValidId(waMessageId, MaxWaMessageIdLength);

    /// <summary>
    /// Lo marca como procesado por el bot, haya respondido o no (un mensaje de hace más de 24 horas, o un aviso de
    /// WhatsApp, se procesa sin respuesta). Conserva el primer momento: un mensaje se procesa una sola vez.
    /// </summary>
    public void MarkProcessed(DateTime nowUtc)
    {
        if (Direction is not WhatsAppMessageDirection.Inbound)
        {
            throw new InvalidOperationException("Only an inbound WhatsApp message is processed by the bot.");
        }

        ProcessedAtUtc ??= nowUtc;
    }

    public static bool IsValidReplyId(string? replyId) => IsValidId(replyId, MaxReplyIdLength);

    /// <summary>
    /// Aplica un aviso de Meta sobre un saliente, solo si es más nuevo que el que tiene: Meta manda los avisos
    /// desordenados y los reintenta. Gana el de hora más nueva; si son del mismo segundo (Meta los manda en segundos),
    /// el que va más adelante en la vida de un mensaje. Un fallo es final. Devuelve si lo aplicó.
    /// </summary>
    public bool ApplyStatus(WhatsAppMessageStatus status, DateTime occurredAtUtc, int? errorCode)
    {
        if (Direction is not WhatsAppMessageDirection.Outbound)
        {
            throw new InvalidOperationException("Only an outbound WhatsApp message has a delivery status.");
        }

        if (!IsNewer(status, occurredAtUtc))
        {
            return false;
        }

        Status = status;
        StatusAtUtc = occurredAtUtc;
        ErrorCode = status is WhatsAppMessageStatus.Failed ? errorCode : null;

        return true;
    }

    private bool IsNewer(WhatsAppMessageStatus status, DateTime occurredAtUtc)
    {
        if (Status is not { } current || StatusAtUtc is not { } currentAtUtc)
        {
            return true;
        }

        if (current is WhatsAppMessageStatus.Failed)
        {
            return false;
        }

        return occurredAtUtc > currentAtUtc || (occurredAtUtc == currentAtUtc && Rank(status) > Rank(current));
    }

    // El orden en la vida de un mensaje, sin depender del número de cada valor del enum.
    private static int Rank(WhatsAppMessageStatus status) => status switch
    {
        WhatsAppMessageStatus.Sent => 1,
        WhatsAppMessageStatus.Delivered => 2,
        WhatsAppMessageStatus.Read => 3,
        WhatsAppMessageStatus.Failed => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown WhatsApp message status."),
    };

    // Un id se usa tal cual (como clave de un índice y de un lock): uno que Postgres no puede guardar no es válido.
    private static bool IsValidId(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && WhatsAppText.IsStorable(value);

    private static string RequireId(string? value, int maxLength, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot be longer than {maxLength} characters.", paramName);
        }

        return WhatsAppText.IsStorable(value)
            ? value
            : throw new ArgumentException("The value cannot have null characters or unpaired surrogates.", paramName);
    }

    private static string? OptionalId(string? value, int maxLength, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireId(value, maxLength, paramName);

    // El texto lo elige la persona: se limpia y se recorta en lugar de rechazar el mensaje.
    private static string? Truncate(string? body) => WhatsAppText.Clean(body, MaxBodyLength);
}
