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
    public static bool IsStorableBody(string body) => StorableText.IsStorable(body);

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
