using System.Globalization;
using System.Text;
using System.Text.Json;
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

    /// <summary>W3: a resend of the form of a send, with what was shared over its prefill (task 23); false in every outgoing of before.</summary>
    public bool IsResend { get; private set; }

    /// <summary>
    /// H31: a rule's notice, which goes after what answers someone (an operator's or the assistant's reply, a tap's confirmation) when
    /// the next chat is chosen; within a chat the order stays. A confirmation shares the FormAction origin, so the mark is its own;
    /// false in every outgoing of before.
    /// </summary>
    public bool IsNotice { get; private set; }

    /// <summary>W3: a queued Flow of a send is its resend, with what was shared over its prefill; marked once, before it goes out.</summary>
    public void MarkResend()
    {
        if (Kind != WhatsAppOutgoingMessageKind.Flow || FlowSessionId is null || State != WhatsAppOutgoingMessageState.Pending || IsResend)
            throw new InvalidOperationException("Only a queued Flow of a send is marked as its resend, once.");
        IsResend = true;
    }
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
        NextAttemptAtUtc = null;
        State = WhatsAppOutgoingMessageState.Sending;
    }

    /// <summary>
    /// H7: when a send that did not go out may try again; meanwhile nothing else of its chat goes out, so the order stays. It
    /// only means something while Pending: reserving clears it, and one already past never holds anything back.
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; private set; }

    /// <summary>H7: how many times this send went back to wait for its next attempt; the worker stops retrying at its limit.</summary>
    public int DispatchRetries { get; private set; }

    /// <summary>
    /// H7: the send did not go out (the network or Meta refused it before it arrived, and it is worth retrying), so it goes back
    /// to Pending in its place until <paramref name="nextAttemptAtUtc"/>. A notice or the assistant also needs its own
    /// reservation, even after its message effect started: the answer said it was not taken. A notice keeps its uploaded
    /// document. Whether it went back.
    /// </summary>
    public bool RetryLater(Guid? reservationId, DateTime nextAttemptAtUtc)
    {
        if (State != WhatsAppOutgoingMessageState.Sending) return false;
        if (Origin is WhatsAppMessageOrigin.FormAction or WhatsAppMessageOrigin.Assistant)
        {
            if (DispatchReservationId is null || DispatchReservationId != reservationId) return false;
            DispatchReservationId = null;
            MessageEffectStartedAtUtc = null;
        }

        State = WhatsAppOutgoingMessageState.Pending;
        StartedAtUtc = null;
        NextAttemptAtUtc = nextAttemptAtUtc;
        DispatchRetries++;
        return true;
    }

    public static WhatsAppOutgoingMessage CreateFlow(Guid actor, Guid contactId, Guid requestId, string body, Guid sessionId, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (body.Length > 1024 || !IsVisibleBody(body)) throw new ArgumentException("The Flow message body is not visible and storable.", nameof(body));
        var outgoing = Create(actor, contactId, requestId, body, nowUtc);
        outgoing.Kind = WhatsAppOutgoingMessageKind.Flow; outgoing.FlowSessionId = sessionId;
        return outgoing;
    }

    /// <summary>W1-A: the location_request_message that goes before the Flow of its send.</summary>
    public static WhatsAppOutgoingMessage CreateLocationRequest(Guid actor, Guid contactId, Guid requestId, string body, Guid sessionId, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (body.Length > 1024 || !IsVisibleBody(body)) throw new ArgumentException("The location request body is not visible and storable.", nameof(body));
        var outgoing = Create(actor, contactId, requestId, body, nowUtc);
        outgoing.Kind = WhatsAppOutgoingMessageKind.LocationRequest; outgoing.FlowSessionId = sessionId;
        return outgoing;
    }

    /// <summary>W1-A: with an optional location, a reply button «Seguir sin ubicación» right after the request; its id names the send.</summary>
    public static WhatsAppOutgoingMessage CreateLocationSkip(Guid actor, Guid contactId, Guid requestId, string body, string title, Guid sessionId, DateTime nowUtc)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(sessionId, Guid.Empty);
        if (body.Length > 1024 || !IsVisibleBody(body)) throw new ArgumentException("The skip body is not visible and storable.", nameof(body));
        if (title.Length > 20 || !IsVisibleBody(title)) throw new ArgumentException("A reply button title is visible and fits 20 characters.", nameof(title));
        var outgoing = Create(actor, contactId, requestId, body, nowUtc);
        outgoing.Kind = WhatsAppOutgoingMessageKind.QuickReplies; outgoing.FlowSessionId = sessionId;
        outgoing.QuickRepliesJson = JsonSerializer.Serialize(new[] { new WhatsAppOutgoingQuickReply(WhatsAppFlowLocationReply.Id(sessionId), title, 0) });
        return outgoing;
    }

    public void AttachFlowSession(WhatsAppFlowSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Kind is not (WhatsAppOutgoingMessageKind.Flow or WhatsAppOutgoingMessageKind.LocationRequest or WhatsAppOutgoingMessageKind.QuickReplies)
            || FlowSessionId != session.Id || FlowSession is not null)
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

    /// <summary>
    /// A person looked at an uncertain send and the conversation goes on (H2: any operator who can send). It does not say that it
    /// arrived nor sends it again; who and when stay, as in the review of a notice.
    /// </summary>
    public void Review(Guid reviewedByUserId, DateTime reviewedAtUtc)
    {
        if (reviewedByUserId == Guid.Empty) throw new ArgumentException("A review is made by someone.", nameof(reviewedByUserId));
        // A notice has its own review, with its decision, and the assistant's uncertainty only admits a late acceptance.
        if (Origin != WhatsAppMessageOrigin.Conversation) throw new InvalidOperationException("Only a send of the chat is reviewed this way.");
        RequireState(WhatsAppOutgoingMessageState.Uncertain);
        State = WhatsAppOutgoingMessageState.Reviewed;
        DeliveryReviewedByUserId = reviewedByUserId;
        DeliveryReviewedAtUtc = reviewedAtUtc;
    }

    /// <summary>
    /// Choices, when present, become reply buttons fixed here with this row's id: a retry or a resend after a crash sends the
    /// same buttons. An action never combines a document with choices.
    /// </summary>
    public static WhatsAppOutgoingMessage CreateFormAction(Guid recipientUserId, Guid contactId, Guid runId, string body,
        DateTime nowUtc, WhatsAppOutgoingDocument? document = null, long contactLinkRevision = 0, string? recipientWaId = null,
        IReadOnlyList<WhatsAppFormActionChoice>? choices = null)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(recipientUserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(contactId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(runId, Guid.Empty);
        var buttons = choices is { Count: > 0 } ? choices : null;
        if (buttons is not null && (document is not null || !WhatsAppFormActionChoice.AreValid(buttons)))
            throw new ArgumentException("Form action choices must be one to three unique, bounded buttons without a document.", nameof(choices));
        if (!IsVisibleBody(body) || body.Length > (document is null && buttons is null ? WhatsAppMessage.MaxBodyLength : 1024))
            throw new ArgumentException("The form action body must be visible and fit the message limit.", nameof(body));
        if (document is not null && (string.IsNullOrWhiteSpace(document.StorageKey) || document.StorageKey.Length > 512
            || string.IsNullOrWhiteSpace(document.Name) || document.Name.Length > 255 || document.ContentType != "application/pdf"
            || document.Size is <= 0 or > 100 * 1024 * 1024 || document.Sha256.Length != 64 || !document.Sha256.All(char.IsAsciiHexDigit)))
            throw new ArgumentException("The form action document metadata is invalid.", nameof(document));
        var outgoing = new WhatsAppOutgoingMessage
        {
            Origin = WhatsAppMessageOrigin.FormAction, IsNotice = true, RecipientUserId = recipientUserId, ContactId = contactId,
            RecipientLinkRevision = contactLinkRevision, RecipientWaId = recipientWaId, RequestId = runId,
            Body = body, RequestedAtUtc = nowUtc, Kind = document is null ? WhatsAppOutgoingMessageKind.Text : WhatsAppOutgoingMessageKind.Document,
            DocumentStorageKey = document?.StorageKey, DocumentName = document?.Name, DocumentContentType = document?.ContentType,
            DocumentSize = document?.Size, DocumentSha256 = document?.Sha256,
        };
        if (buttons is not null)
        {
            outgoing.Kind = WhatsAppOutgoingMessageKind.QuickReplies;
            outgoing.QuickRepliesJson = JsonSerializer.Serialize(buttons
                .Select(choice => new WhatsAppOutgoingQuickReply(WhatsAppFormActionChoice.ReplyId(outgoing.Id, choice.Id), choice.Title, 0)).ToArray());
        }

        return outgoing;
    }

    /// <summary>
    /// The text an owner hands back for an applied tap: a form action text with the tap as its request and the checks of any notice,
    /// but it answers the person, so it goes before the rules' notices when the next chat is chosen (H31).
    /// </summary>
    public static WhatsAppOutgoingMessage CreateFormActionReply(Guid recipientUserId, Guid contactId, Guid tapId, string body, DateTime nowUtc,
        long contactLinkRevision, string? recipientWaId)
    {
        var outgoing = CreateFormAction(recipientUserId, contactId, tapId, body, nowUtc, null, contactLinkRevision, recipientWaId);
        outgoing.IsNotice = false;
        return outgoing;
    }

    /// <summary>Whether this action offers exactly these choices, in order; no choices match an action without buttons.</summary>
    public bool HasFormActionChoices(IReadOnlyList<WhatsAppFormActionChoice>? choices) =>
        Origin == WhatsAppMessageOrigin.FormAction && QuickReplies
            .Select(reply => WhatsAppFormActionChoice.TryParseReplyId(reply.Id, out var outgoingId, out var choiceId) && outgoingId == Id
                ? new WhatsAppFormActionChoice(choiceId, reply.Title) : null)
            .SequenceEqual(choices ?? []);

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

    /// <summary>
    /// H8: the send stopped before Meta was called (the host stopped while it waited for its turn to write to this person, or
    /// while it prepared it), so it goes back to Pending in its place, nothing sent. For the chat it is a transition of its own;
    /// a notice or the assistant also needs its reservation and no started message effect, as when its preparation expires. The
    /// caller vouches that Meta was not called. H3: with <paramref name="nextAttemptAtUtc"/> (the turn to write to this person could
    /// not be taken, as with Redis down) it waits until then, without spending one of its retries. Whether it went back.
    /// </summary>
    public bool ReturnUnsent(Guid? reservationId, DateTime? nextAttemptAtUtc = null)
    {
        if (State != WhatsAppOutgoingMessageState.Sending) return false;
        if (Origin is WhatsAppMessageOrigin.FormAction or WhatsAppMessageOrigin.Assistant)
        {
            if (DispatchReservationId is null || DispatchReservationId != reservationId || MessageEffectStartedAtUtc.HasValue) return false;
            DispatchReservationId = null;
        }

        State = WhatsAppOutgoingMessageState.Pending;
        StartedAtUtc = null;
        NextAttemptAtUtc = nextAttemptAtUtc;
        return true;
    }

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
        // H7: a manual retry is a new round of attempts, with no wait left.
        NextAttemptAtUtc = null;
        DispatchRetries = 0;
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
