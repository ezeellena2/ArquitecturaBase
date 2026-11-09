using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// Una emisión pedida a la cola (ADR 0028): los datos fijados al pedirla, el número y el hash que se guardan antes de llamar
/// a <c>FECAESolicitar</c>, y lo que se supo después. La clave del pedido (el run de la acción que la pidió) es única: pedir
/// dos veces la misma emisión da la misma fila. Las transiciones del worker que no corresponden son un error del sistema;
/// las de una persona (revisar y cancelar) devuelven su error.
/// </summary>
public sealed class ArcaVoucher : Entity
{
    private ArcaVoucher() { }

    /// <summary>La conexión que la pidió. Sin clave foránea: desconectar no borra comprobantes, que tienen valor legal.</summary>
    public Guid ConnectionId { get; private set; }
    public Guid RequestKey { get; private set; }
    public ArcaEnvironment Environment { get; private set; }
    public ArcaVoucherClass Class { get; private set; }
    public ArcaVoucherKind Kind { get; private set; }
    public int VoucherType { get; private set; }
    public string Cuit { get; private set; } = string.Empty;
    public int PointOfSale { get; private set; }
    public int Concept { get; private set; }
    public int DocumentType { get; private set; }
    public long DocumentNumber { get; private set; }
    public DateOnly RequestedOn { get; private set; }
    public decimal Total { get; private set; }

    /// <summary>Lo demás que se fijó al pedir (receptor, renglones, fechas): lo arma y lo lee Application.</summary>
    public string PayloadJson { get; private set; } = "{}";

    /// <summary>En una nota de crédito, la emisión de la factura que anula.</summary>
    public Guid? CancelsVoucherId { get; private set; }

    public ArcaVoucherState State { get; private set; }
    public ArcaVoucherReviewReason? ReviewReason { get; private set; }
    public long? Number { get; private set; }
    public string? DataHash { get; private set; }
    public string? Cae { get; private set; }
    public DateOnly? CaeDueOn { get; private set; }
    public DateOnly? IssuedOn { get; private set; }
    public string? FailureCode { get; private set; }

    /// <summary>Los códigos y textos de ARCA del último intento (errores u observaciones), en JSON.</summary>
    public string? MessagesJson { get; private set; }

    public int Failures { get; private set; }
    public bool WaitedForArca { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>El PDF fiscal en el almacén privado: se arma una vez autorizado y nunca provoca otra emisión.</summary>
    public string? PdfStorageKey { get; private set; }
    public string? PdfSha256 { get; private set; }
    public long? PdfSize { get; private set; }

    /// <summary>Cambia con cada transición: una revisión humana la espera para no decidir sobre algo que ya cambió.</summary>
    public int Revision { get; private set; }

    /// <summary>Tiene un número que ARCA puede haber usado: su secuencia no da otro hasta resolverlo.</summary>
    public bool HoldsNumber => Number is not null && State is ArcaVoucherState.Sending or ArcaVoucherState.Uncertain or ArcaVoucherState.NeedsReview;

    public static ArcaVoucher Create(Guid connectionId, ArcaEnvironment environment, Guid requestKey, ArcaVoucherClass voucherClass, ArcaVoucherKind kind, string cuit, int pointOfSale,
        int concept, int documentType, long documentNumber, DateOnly requestedOn, decimal total, string payloadJson, Guid? cancelsVoucherId, DateTime nowUtc) =>
        new()
        {
            ConnectionId = connectionId,
            Environment = environment,
            RequestKey = requestKey,
            Class = voucherClass,
            Kind = kind,
            VoucherType = ArcaVoucherTypes.Code(voucherClass, kind),
            Cuit = cuit,
            PointOfSale = pointOfSale,
            Concept = concept,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            RequestedOn = requestedOn,
            Total = total,
            PayloadJson = payloadJson,
            CancelsVoucherId = cancelsVoucherId,
            State = ArcaVoucherState.Queued,
            NextAttemptAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
        };

    /// <summary>
    /// Le toca un intento: en cola o incierto con su espera cumplida, o «enviando» de un host que se cayó (venció la reserva
    /// con la que pidió).
    /// </summary>
    public bool IsDue(DateTime nowUtc) =>
        State is ArcaVoucherState.Queued or ArcaVoucherState.Uncertain or ArcaVoucherState.Sending && NextAttemptAtUtc <= nowUtc;

    /// <summary>Autorizado, todavía sin su PDF fiscal, y ya pasó su espera.</summary>
    public bool IsPdfDue(DateTime nowUtc) => State == ArcaVoucherState.Authorized && PdfStorageKey is null && NextAttemptAtUtc <= nowUtc;

    /// <summary>
    /// Guarda el número y la huella antes de pedir. Si para <paramref name="reconcileAfterUtc"/> (cuando vence la reserva de la
    /// secuencia) no se supo qué pasó, el host se cayó y el comprobante se reconcilia.
    /// </summary>
    public void AssignNumber(long number, string dataHash, DateTime reconcileAfterUtc)
    {
        Require(State == ArcaVoucherState.Queued && Number is null, "assign a number");
        (Number, DataHash, State, NextAttemptAtUtc) = (number, dataHash, ArcaVoucherState.Sending, reconcileAfterUtc);
        Revision++;
    }

    /// <summary>La reconciliación dijo que ARCA no lo emitió: se vuelve a pedir con el mismo número.</summary>
    public void Resend(DateTime reconcileAfterUtc)
    {
        Require(State is ArcaVoucherState.Uncertain or ArcaVoucherState.Sending && Number is not null, "resend");
        (State, NextAttemptAtUtc) = (ArcaVoucherState.Sending, reconcileAfterUtc);
        Revision++;
    }

    public void Authorize(string cae, DateOnly caeDueOn, string? messagesJson, DateTime nowUtc)
    {
        Require(State is ArcaVoucherState.Sending or ArcaVoucherState.Uncertain && Number is not null, "authorize");
        (Cae, CaeDueOn, IssuedOn, MessagesJson, FailureCode) = (cae, caeDueOn, RequestedOn, messagesJson, null);
        (Failures, NextAttemptAtUtc) = (0, nowUtc);
        Complete(ArcaVoucherState.Authorized, nowUtc);
    }

    public void Reject(string failureCode, string? messagesJson, DateTime nowUtc)
    {
        Require(State == ArcaVoucherState.Sending, "reject");
        (Number, DataHash, FailureCode, MessagesJson) = (null, null, failureCode, messagesJson);
        Complete(ArcaVoucherState.Rejected, nowUtc);
    }

    /// <summary>Con número y sin saber si se emitió: espera y se reconcilia en el próximo intento.</summary>
    public void MarkUncertain(string failureCode, DateTime nowUtc)
    {
        Require(State is ArcaVoucherState.Sending or ArcaVoucherState.Uncertain && Number is not null, "mark as uncertain");
        State = ArcaVoucherState.Uncertain;
        Wait(failureCode, nowUtc);
    }

    /// <summary>No se pudo intentar (sin ticket, sin conexión activa o ARCA sin responder antes de dar número).</summary>
    public void Postpone(string failureCode, DateTime nowUtc)
    {
        Require(State is ArcaVoucherState.Queued or ArcaVoucherState.Uncertain, "postpone");
        Wait(failureCode, nowUtc);
    }

    public void RequireReview(ArcaVoucherReviewReason reason, DateTime nowUtc)
    {
        Require(reason == ArcaVoucherReviewReason.DateOutOfRange
            ? State == ArcaVoucherState.Queued && Number is null
            : State is ArcaVoucherState.Sending or ArcaVoucherState.Uncertain && Number is not null, "require a review");
        (State, ReviewReason, NextAttemptAtUtc) = (ArcaVoucherState.NeedsReview, reason, nowUtc);
        Revision++;
    }

    public Result Review(ArcaVoucherReviewDecision decision, int expectedRevision, DateOnly today, DateTime nowUtc)
    {
        if (State != ArcaVoucherState.NeedsReview) return ArcaInvoicingErrors.VoucherNotInReview;
        if (expectedRevision != Revision) return ArcaInvoicingErrors.VoucherRevisionConflict;
        switch (decision)
        {
            case ArcaVoucherReviewDecision.DoNotIssue:
                (Number, DataHash) = (null, null);
                Complete(ArcaVoucherState.Cancelled, nowUtc);
                return Result.Success();
            case ArcaVoucherReviewDecision.IssueWithNextNumber when ReviewReason is ArcaVoucherReviewReason.NumberTaken or ArcaVoucherReviewReason.NumberMissing:
                (Number, DataHash) = (null, null);
                Requeue(nowUtc);
                return Result.Success();
            case ArcaVoucherReviewDecision.IssueToday when ReviewReason == ArcaVoucherReviewReason.DateOutOfRange:
                RequestedOn = today;
                Requeue(nowUtc);
                return Result.Success();
            default:
                return ArcaInvoicingErrors.ReviewDecisionInvalid;
        }
    }

    /// <summary>Sólo sin número: una vez guardado, ARCA puede haberlo usado y hay que resolverlo.</summary>
    public Result Cancel(DateTime nowUtc)
    {
        if (Number is not null || State is not (ArcaVoucherState.Queued or ArcaVoucherState.NeedsReview)) return ArcaInvoicingErrors.VoucherNotCancellable;
        Complete(ArcaVoucherState.Cancelled, nowUtc);
        return Result.Success();
    }

    public void AttachPdf(string storageKey, string sha256, long size)
    {
        Require(State == ArcaVoucherState.Authorized && PdfStorageKey is null, "attach its PDF");
        (PdfStorageKey, PdfSha256, PdfSize, FailureCode) = (storageKey, sha256, size, null);
        Revision++;
    }

    /// <summary>El almacén falló al guardar el PDF: el comprobante sigue autorizado y el PDF se reintenta con espera.</summary>
    public void PostponePdf(string failureCode, DateTime nowUtc)
    {
        Require(State == ArcaVoucherState.Authorized && PdfStorageKey is null, "postpone its PDF");
        Wait(failureCode, nowUtc);
    }

    public override string ToString() => $"ArcaVoucher {{ id: {Id}, type: {VoucherType}, pointOfSale: {PointOfSale}, number: {Number}, state: {State} }}";

    private void Wait(string failureCode, DateTime nowUtc)
    {
        Failures++;
        (FailureCode, WaitedForArca, NextAttemptAtUtc) = (failureCode, true, nowUtc + ArcaRetrySchedule.After(Failures));
        Revision++;
    }

    private void Requeue(DateTime nowUtc)
    {
        (State, ReviewReason, Failures, FailureCode, NextAttemptAtUtc) = (ArcaVoucherState.Queued, null, 0, null, nowUtc);
        Revision++;
    }

    private void Complete(ArcaVoucherState state, DateTime nowUtc)
    {
        (State, ReviewReason, CompletedAtUtc) = (state, null, nowUtc);
        Revision++;
    }

    private void Require(bool allowed, string transition)
    {
        if (!allowed) throw new InvalidOperationException($"An ARCA voucher in {State} cannot {transition}.");
    }
}
