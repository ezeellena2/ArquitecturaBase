using System.Globalization;
using System.Text;
using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>A durable manual send. Once dispatch starts, a lost result cannot trigger an automatic resend.</summary>
public sealed class WhatsAppOutgoingMessage : Entity
{
    private WhatsAppOutgoingMessage() { }
    public Guid OperatorUserId { get; private set; }
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
        if (State is not (WhatsAppOutgoingMessageState.Sending or WhatsAppOutgoingMessageState.Uncertain or WhatsAppOutgoingMessageState.Reviewed)
            || !WhatsAppMessage.IsValidWaMessageId(waMessageId))
        {
            throw new InvalidOperationException("Only an in-flight send can be accepted with a valid provider id.");
        }

        WaMessageId = waMessageId;
        State = WhatsAppOutgoingMessageState.Accepted;
    }

    public void Fail()
    {
        if (State is not (WhatsAppOutgoingMessageState.Pending or WhatsAppOutgoingMessageState.Sending))
        {
            throw new InvalidOperationException("Only a pending or in-flight send can be rejected.");
        }

        State = WhatsAppOutgoingMessageState.Failed;
    }

    public void MarkUncertain()
    {
        RequireState(WhatsAppOutgoingMessageState.Sending);
        State = WhatsAppOutgoingMessageState.Uncertain;
    }

    public void Review()
    {
        RequireState(WhatsAppOutgoingMessageState.Uncertain);
        State = WhatsAppOutgoingMessageState.Reviewed;
    }

    private void RequireState(WhatsAppOutgoingMessageState expected)
    {
        if (State != expected)
        {
            throw new InvalidOperationException("The outgoing message cannot repeat or overwrite a terminal transition.");
        }
    }
}
