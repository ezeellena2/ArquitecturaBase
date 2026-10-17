using System.Globalization;
using System.Text;
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>A durable queued send. Once the message effect starts, a lost result cannot trigger an automatic resend.</summary>
public sealed partial class WhatsAppOutgoingMessage : Entity
{
    private WhatsAppOutgoingMessage() { }
    public Guid? OperatorUserId { get; private set; }
    public WhatsAppMessageOrigin Origin { get; private set; }
    public Guid? RecipientUserId { get; private set; }
    public long? RecipientLinkRevision { get; private set; }
    public string? RecipientWaId { get; private set; }
    public Guid? DispatchReservationId { get; private set; }
    public DateTime? MessageEffectStartedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public bool? DeliveryReviewWasSent { get; private set; }
    public long LastFormActionControlRevision { get; private set; }
    public Guid? DeliveryReviewedByUserId { get; private set; }
    public DateTime? DeliveryReviewedAtUtc { get; private set; }
    public string? DocumentStorageKey { get; private set; }
    public string? DocumentName { get; private set; }
    public string? DocumentContentType { get; private set; }
    public long? DocumentSize { get; private set; }
    public string? DocumentSha256 { get; private set; }
    public string? DocumentMediaId { get; private set; }
    public WhatsAppOutgoingDocument? Document => DocumentStorageKey is null ? null : new(DocumentStorageKey, DocumentName!, DocumentContentType!, DocumentSize!.Value, DocumentSha256!);
    public Guid ContactId { get; private set; }
    public Guid RequestId { get; private set; }
    public string? Body { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public WhatsAppOutgoingMessageState State { get; private set; }
    public string? WaMessageId { get; private set; }
    public WhatsAppOutgoingMessageKind Kind { get; private set; }
    public Guid? FlowSessionId { get; private set; }
    public WhatsAppFlowSession? FlowSession { get; private set; }
    public static bool IsStorableBody(string body) => StorableText.IsStorable(body);
    public static bool IsVisibleBody(string body)
    {
        if (!StorableText.IsStorable(body)) return false;
        var hasVisibleRune = false;
        foreach (var rune in body.EnumerateRunes())
        {
            if (rune.Value is '\t' or '\r' or '\n') continue;
            var category = Rune.GetUnicodeCategory(rune);
            if (category == UnicodeCategory.Control
                || category == UnicodeCategory.Format && rune.Value is not (0x200C or 0x200D)
                || category is UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned)
                return false;
            if (!Rune.IsWhiteSpace(rune) && category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format))
                hasVisibleRune = true;
        }

        return hasVisibleRune;
    }

    public static WhatsAppOutgoingMessage Create(Guid operatorUserId, Guid contactId, Guid requestId, string body, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operatorUserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(contactId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (body.Length > WhatsAppMessage.MaxBodyLength || !StorableText.IsStorable(body))
        {
            throw new ArgumentException("The message must be storable and fit the provider limit.", nameof(body));
        }

        return new WhatsAppOutgoingMessage
        {
            OperatorUserId = operatorUserId, ContactId = contactId, RequestId = requestId,
            Body = body, RequestedAtUtc = nowUtc,
        };
    }

    public void Start(DateTime nowUtc)
    {
        RequireState(WhatsAppOutgoingMessageState.Pending);
        StartedAtUtc = nowUtc;
        State = WhatsAppOutgoingMessageState.Sending;
    }

    public static WhatsAppOutgoingMessage CreateFlow(Guid actor, Guid contactId, Guid requestId, string body, Guid sessionId, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (body.Length > 1024 || !IsVisibleBody(body)) throw new ArgumentException("The Flow message body is not visible and storable.", nameof(body));
        var outgoing = Create(actor, contactId, requestId, body, nowUtc);
        outgoing.Kind = WhatsAppOutgoingMessageKind.Flow; outgoing.FlowSessionId = sessionId;
        return outgoing;
    }

    public void AttachFlowSession(WhatsAppFlowSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Kind != WhatsAppOutgoingMessageKind.Flow || FlowSessionId != session.Id || FlowSession is not null)
            throw new InvalidOperationException("A Flow outgoing message must reference its one matching session.");
        FlowSession = session;
    }

    public void Accept(string waMessageId)
    {
        if ((State is not (WhatsAppOutgoingMessageState.Sending or WhatsAppOutgoingMessageState.Uncertain or WhatsAppOutgoingMessageState.Reviewed)
                && !(Origin == WhatsAppMessageOrigin.FormAction && State == WhatsAppOutgoingMessageState.Failed && DeliveryReviewWasSent == false))
            || !WhatsAppMessage.IsValidWaMessageId(waMessageId))
        {
            throw new InvalidOperationException("Only an in-flight send can be accepted with a valid provider id.");
        }

        WaMessageId = waMessageId;
        FailureCode = null;
        State = WhatsAppOutgoingMessageState.Accepted;
    }

    public void Fail(string? failureCode = null)
    {
        if (State is not (WhatsAppOutgoingMessageState.Pending or WhatsAppOutgoingMessageState.Sending))
        {
            throw new InvalidOperationException("Only a pending or in-flight send can be rejected.");
        }

        FailureCode = failureCode;
        State = WhatsAppOutgoingMessageState.Failed;
    }

    public void MarkUncertain()
    {
        RequireState(WhatsAppOutgoingMessageState.Sending);
        State = WhatsAppOutgoingMessageState.Uncertain;
    }

    public bool ReviewFormAction(long expectedRevision, bool wasSent, Guid reviewedByUserId, DateTime reviewedAtUtc)
    {
        if (Origin != WhatsAppMessageOrigin.FormAction || State != WhatsAppOutgoingMessageState.Uncertain || reviewedByUserId == Guid.Empty || expectedRevision <= LastFormActionControlRevision) return false;
        LastFormActionControlRevision = expectedRevision;
        DeliveryReviewWasSent = wasSent; DeliveryReviewedByUserId = reviewedByUserId; DeliveryReviewedAtUtc = reviewedAtUtc;
        State = wasSent ? WhatsAppOutgoingMessageState.Reviewed : WhatsAppOutgoingMessageState.Failed;
        FailureCode = wasSent ? null : WhatsAppFormActionErrors.ReviewedNotSent.Code;
        return true;
    }

    public void Review()
    {
        RequireState(WhatsAppOutgoingMessageState.Uncertain);
        State = WhatsAppOutgoingMessageState.Reviewed;
    }

    public static WhatsAppOutgoingMessage CreateFormAction(Guid recipientUserId, Guid contactId, Guid runId, string body,
        DateTime nowUtc, WhatsAppOutgoingDocument? document = null, long contactLinkRevision = 0, string? recipientWaId = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(recipientUserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(contactId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(runId, Guid.Empty);
        if (!IsVisibleBody(body) || body.Length > (document is null ? WhatsAppMessage.MaxBodyLength : 1024))
            throw new ArgumentException("The form action body must be visible and fit the message limit.", nameof(body));
        if (document is not null && (string.IsNullOrWhiteSpace(document.StorageKey) || document.StorageKey.Length > 512
            || string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 255 || document.ContentType != "application/pdf"
            || document.Size is <= 0 or > 100 * 1024 * 1024 || document.Sha256.Length != 64 || !document.Sha256.All(char.IsAsciiHexDigit)))
            throw new ArgumentException("The form action document metadata is invalid.", nameof(document));
        return new WhatsAppOutgoingMessage
        {
            Origin = WhatsAppMessageOrigin.FormAction, RecipientUserId = recipientUserId, ContactId = contactId,
            RecipientLinkRevision = contactLinkRevision, RecipientWaId = recipientWaId, RequestId = runId,
            Body = body, RequestedAtUtc = nowUtc, Kind = document is null ? WhatsAppOutgoingMessageKind.Text : WhatsAppOutgoingMessageKind.Document,
            DocumentStorageKey = document?.StorageKey, DocumentName = document?.Name, DocumentContentType = document?.ContentType,
            DocumentSize = document?.Size, DocumentSha256 = document?.Sha256,
        };
    }

    public Guid ReserveFormAction(DateTime nowUtc) => ReserveSystemDispatch(WhatsAppMessageOrigin.FormAction, nowUtc);

    public bool OwnsFormActionReservation(Guid reservationId) => OwnsSystemReservation(WhatsAppMessageOrigin.FormAction, reservationId);

    public bool RecordFormActionMedia(Guid reservationId, string mediaId)
    {
        if (!OwnsFormActionReservation(reservationId) || MessageEffectStartedAtUtc.HasValue) return false;
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaId);
        if (mediaId.Length > 128 || !mediaId.All(char.IsAsciiDigit)) throw new ArgumentException("The media id is invalid.", nameof(mediaId));
        DocumentMediaId = mediaId;
        return true;
    }

    public bool MarkFormActionMessageEffect(Guid reservationId, DateTime nowUtc) =>
        MarkSystemMessageEffect(WhatsAppMessageOrigin.FormAction, reservationId, nowUtc);

    public void RecoverExpiredFormAction() => RecoverExpiredSystemDispatch(WhatsAppMessageOrigin.FormAction);

    private Guid ReserveSystemDispatch(WhatsAppMessageOrigin origin, DateTime nowUtc)
    {
        if (Origin != origin) throw new InvalidOperationException("The outgoing origin does not own this system dispatch.");
        Start(nowUtc); DispatchReservationId = Guid.NewGuid(); MessageEffectStartedAtUtc = null;
        return DispatchReservationId.Value;
    }

    private bool OwnsSystemReservation(WhatsAppMessageOrigin origin, Guid reservationId) =>
        Origin == origin && DispatchReservationId == reservationId && State == WhatsAppOutgoingMessageState.Sending;

    private bool MarkSystemMessageEffect(WhatsAppMessageOrigin origin, Guid reservationId, DateTime nowUtc)
    {
        if (!OwnsSystemReservation(origin, reservationId) || MessageEffectStartedAtUtc.HasValue) return false;
        MessageEffectStartedAtUtc = nowUtc; return true;
    }

    private void RecoverExpiredSystemDispatch(WhatsAppMessageOrigin origin)
    {
        if (Origin != origin) throw new InvalidOperationException("The outgoing origin does not own this preparation phase.");
        RequireState(WhatsAppOutgoingMessageState.Sending);
        if (MessageEffectStartedAtUtc.HasValue) MarkUncertain();
        else { State = WhatsAppOutgoingMessageState.Pending; StartedAtUtc = null; DispatchReservationId = null; }
    }

    public bool RetryFormAction(long controlRevision, DateTime nowUtc)
    {
        if (Origin != WhatsAppMessageOrigin.FormAction || State != WhatsAppOutgoingMessageState.Failed || controlRevision <= LastFormActionControlRevision) return false;
        LastFormActionControlRevision = controlRevision;
        State = WhatsAppOutgoingMessageState.Pending;
        RequestedAtUtc = nowUtc;
        StartedAtUtc = null;
        MessageEffectStartedAtUtc = null;
        DispatchReservationId = null;
        FailureCode = null;
        DocumentMediaId = null;
        return true;
    }

    private void RequireState(WhatsAppOutgoingMessageState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException("The outgoing message cannot repeat or overwrite a terminal transition.");
        }
    }
}
