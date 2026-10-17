using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// Un mensaje de WhatsApp, entrante o saliente (sección 6.5 del spec del ingreso con WhatsApp). El
/// <see cref="WaMessageId"/> es único y es la defensa contra los duplicados: Meta reintenta durante días, y un mensaje
/// que ya está guardado no se vuelve a guardar. No se guarda el payload crudo, y un saliente con un código o un enlace
/// se guarda con su resumen seguro ("[código]"), nunca con el código.
/// </summary>
public sealed partial class WhatsAppMessage : Entity
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
        DateTime occurredAtUtc,
        byte[]? protectedFlowResponse)
    {
        ContactId = contactId;
        Direction = direction;
        WaMessageId = waMessageId;
        Kind = kind;
        Body = body;
        ReplyId = replyId;
        OccurredAtUtc = occurredAtUtc;
        ProtectedFlowResponse = protectedFlowResponse?.ToArray();
    }

    /// <summary>
    /// Quién lo mandó o a quién se le mandó. Un entrante siempre tiene contacto; un saliente puede no tenerlo, como un
    /// código de ingreso mandado a un número que nunca escribió al WhatsApp.
    /// </summary>
    public Guid? ContactId { get; private set; }

    public WhatsAppMessageOrigin Origin { get; private set; }

    public WhatsAppMessageDirection Direction { get; private set; }

    /// <summary>El id de Meta (<c>wamid.…</c>). Único.</summary>
    public string WaMessageId { get; private set; }

    public WhatsAppMessageKind Kind { get; private set; }

    public WhatsAppMessagePurpose Purpose { get; private set; }
    public WhatsAppAccessEventKind? AccessEventKind { get; private set; }
    public Guid? FlowSessionId { get; private set; }
    public WhatsAppFlowSession? FlowSession { get; private set; }
    public WhatsAppFlowCaptureState? FlowCaptureState { get; private set; }
    public Guid? FlowRecordId { get; private set; }
    public string? FlowRecordNumber { get; private set; }
    public string? FlowCaptureErrorCode { get; private set; }
    public string? FlowRejectedQuestion { get; private set; }
    public bool HasFlowFiles { get; private set; }
    public int? FlowFileCount { get; private set; }
    public void MarkFlowFiles(int? fileCount = null)
    {
        if (Kind != WhatsAppMessageKind.FlowReply) throw new InvalidOperationException("Only a Flow reply can contain form files.");
        if (fileCount < 0) throw new ArgumentOutOfRangeException(nameof(fileCount));
        HasFlowFiles = true;
        FlowFileCount = fileCount ?? FlowFileCount;
    }
    public int FlowReplyFailureCount { get; private set; }
    public DateTime? FlowReplyFirstFailureAtUtc { get; private set; }
    public const int MaxFlowReplyFailures = 5;

    public bool RegisterFlowReplyFailure(DateTime nowUtc)
    {
        if (Kind != WhatsAppMessageKind.FlowReply || Direction != WhatsAppMessageDirection.Inbound)
            throw new InvalidOperationException("Only an inbound Flow reply can retry capture.");
        if (ProcessedAtUtc.HasValue || FlowCaptureState.HasValue) return false;
        FlowReplyFirstFailureAtUtc ??= nowUtc;
        FlowReplyFailureCount = Math.Min(FlowReplyFailureCount + 1, MaxFlowReplyFailures);
        return FlowReplyFailureCount == MaxFlowReplyFailures
            && nowUtc - FlowReplyFirstFailureAtUtc.Value >= TimeSpan.FromMinutes(15);
    }

    public void AttachFlowSession(Guid sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (FlowSessionId == sessionId) return;
        if (!((Kind == WhatsAppMessageKind.Flow && Direction == WhatsAppMessageDirection.Outbound)
            || (Kind == WhatsAppMessageKind.FlowReply && Direction == WhatsAppMessageDirection.Inbound)) || FlowSessionId.HasValue)
            throw new InvalidOperationException("Only a Flow message can attach its session once.");
        FlowSessionId = sessionId;
    }

    public bool SetFlowReplyCapture(WhatsAppFlowCaptureState state, Guid? recordId, string? number, string? errorCode, string? rejectedQuestion = null)
    {
        if (Kind != WhatsAppMessageKind.FlowReply || Direction != WhatsAppMessageDirection.Inbound)
            throw new InvalidOperationException("Only an inbound Flow reply can store its capture result.");
        if (state is WhatsAppFlowCaptureState.Pending || !Enum.IsDefined(state)
            || (state == WhatsAppFlowCaptureState.Completed) != (recordId.HasValue && number is not null)
            || errorCode?.Length > 128 || number?.Length > 64)
            throw new ArgumentException("The Flow reply capture result is invalid.", nameof(state));
        if (FlowCaptureState.HasValue)
            return FlowCaptureState == state && FlowRecordId == recordId
                && FlowRecordNumber == number && FlowCaptureErrorCode == errorCode;
        FlowCaptureState = state;
        FlowRecordId = recordId;
        FlowRecordNumber = number;
        FlowCaptureErrorCode = errorCode;
        FlowRejectedQuestion = state == WhatsAppFlowCaptureState.Rejected ? rejectedQuestion : null;
        return true;
    }

    public void Classify(WhatsAppMessagePurpose purpose, WhatsAppAccessEventKind? accessEventKind = null)
    {
        if (!Enum.IsDefined(purpose) || (accessEventKind is { } kind && !Enum.IsDefined(kind))
            || (purpose == WhatsAppMessagePurpose.Access) != accessEventKind.HasValue)
        {
            throw new ArgumentException("Access messages require an access event kind; other purposes cannot have one.", nameof(purpose));
        }

        Purpose = purpose;
        AccessEventKind = accessEventKind;
    }

    /// <summary>
    /// El texto, el título del botón o el aviso de WhatsApp; null en una foto o un audio. En un saliente, su resumen
    /// seguro. Se recorta a <see cref="MaxBodyLength"/>. También es null en cualquier mensaje viejo: la retención (90 días
    /// por defecto) borra el texto y deja el resto de la fila, así que nada puede contar con que el texto siga ahí.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>
    /// El identificador del botón que se tocó: el id de un botón de respuesta o el payload de un botón de plantilla. Es
    /// con lo que la política clasifica una acción estructurada.
    /// </summary>
    public string? ReplyId { get; private set; }

    /// <summary>Respuesta nfm_reply protegida y ligada al mensaje/contacto; nunca forma parte del DTO del chat.</summary>
    public byte[]? ProtectedFlowResponse { get; private set; }

    /// <summary>Cuándo se mandó, con la hora de Meta en un entrante y la nuestra en un saliente.</summary>
    public DateTime OccurredAtUtc { get; private set; }

    public long InboundSequence { get; private set; }
    public Guid? OperatorUserId { get; private set; }

    public void SetInboundSequence(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        if (Direction != WhatsAppMessageDirection.Inbound || InboundSequence != 0)
        {
            throw new InvalidOperationException("An inbound receive sequence is assigned only once.");
        }

        InboundSequence = sequence;
    }

    public void AttributeFormAction()
    {
        if (Direction != WhatsAppMessageDirection.Outbound || OperatorUserId.HasValue)
            throw new InvalidOperationException("Only an outbound system message can belong to a form action.");
        Origin = WhatsAppMessageOrigin.FormAction;
        Classify(WhatsAppMessagePurpose.Business);
    }

    public void AttributeOperator(Guid operatorUserId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operatorUserId, Guid.Empty);
        if (Direction != WhatsAppMessageDirection.Outbound)
        {
            throw new InvalidOperationException("Only an outbound message can have an operator author.");
        }

        OperatorUserId = operatorUserId;
        Classify(WhatsAppMessagePurpose.Business);
    }

    /// <summary>
    /// Cuándo lo procesó el servicio entrante. Null en un entrante pendiente, y siempre en un saliente: los pendientes tienen un
    /// índice propio, así que marcar uno lo saca de la lista que revisa el procesador.
    /// </summary>
    public DateTime? ProcessedAtUtc { get; private set; }

    /// <summary>El último estado que avisó Meta de un saliente; null si todavía no avisó ninguno.</summary>
    public WhatsAppMessageStatus? Status { get; private set; }

    /// <summary>Cuándo pasó <see cref="Status"/>, con la hora de Meta.</summary>
    public DateTime? StatusAtUtc { get; private set; }

    /// <summary>El código de error de Meta de un saliente que falló (por ejemplo, 131026).</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>Un mensaje que mandó la persona. Queda pendiente de procesamiento.</summary>
    public static WhatsAppMessage Inbound(
        Guid contactId,
        string waMessageId,
        WhatsAppMessageKind kind,
        string? body,
        string? replyId,
        DateTime occurredAtUtc,
        byte[]? protectedFlowResponse = null)
    {
        if (contactId == Guid.Empty)
        {
            throw new ArgumentException("An inbound WhatsApp message needs its contact.", nameof(contactId));
        }

        if ((kind is WhatsAppMessageKind.FlowReply) != (protectedFlowResponse is { Length: > 0 })
            || (kind is WhatsAppMessageKind.FlowReply && (body is not null || replyId is not null)))
        {
            throw new ArgumentException("A Flow reply must contain only a protected response payload.", nameof(protectedFlowResponse));
        }

        return new WhatsAppMessage(
            contactId,
            WhatsAppMessageDirection.Inbound,
            RequireId(waMessageId, MaxWaMessageIdLength, nameof(waMessageId)),
            kind,
            Truncate(body),
            OptionalId(replyId, MaxReplyIdLength, nameof(replyId)),
            occurredAtUtc,
            protectedFlowResponse);
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
            sentAtUtc,
            protectedFlowResponse: null);
    }

    public static bool IsValidWaMessageId(string? waMessageId) => IsValidId(waMessageId, MaxWaMessageIdLength);

    /// <summary>
    /// Lo marca como procesado por el servicio entrante, haya respondido o no (un mensaje de hace más de 24 horas, o un aviso de
    /// WhatsApp, se procesa sin respuesta). Conserva el primer momento: un mensaje se procesa una sola vez.
    /// </summary>
    public void MarkProcessed(DateTime nowUtc)
    {
        if (Direction is not WhatsAppMessageDirection.Inbound)
        {
            throw new InvalidOperationException("Only an inbound WhatsApp message is processed by the inbound service.");
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

    private bool IsNewer(WhatsAppMessageStatus status, DateTime occurredAtUtc) =>
        ShouldApplyStatus(Status, StatusAtUtc, status, occurredAtUtc);

    internal static bool ShouldApplyStatus(WhatsAppMessageStatus? currentStatus, DateTime? currentTimestamp, WhatsAppMessageStatus status, DateTime occurredAtUtc)
    {
        if (currentStatus is not { } current || currentTimestamp is not { } currentAtUtc)
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
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && StorableText.IsStorable(value);

    private static string RequireId(string? value, int maxLength, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"The value cannot be longer than {maxLength} characters.", paramName);
        }

        return StorableText.IsStorable(value)
            ? value
            : throw new ArgumentException("The value cannot have null characters or unpaired surrogates.", paramName);
    }

    private static string? OptionalId(string? value, int maxLength, string paramName) =>
        string.IsNullOrWhiteSpace(value) ? null : RequireId(value, maxLength, paramName);

    // El texto lo elige la persona: se limpia y se recorta en lugar de rechazar el mensaje.
    private static string? Truncate(string? body) => StorableText.Clean(body, MaxBodyLength);
}
