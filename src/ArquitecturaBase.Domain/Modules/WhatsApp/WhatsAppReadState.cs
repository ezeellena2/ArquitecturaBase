using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public sealed class WhatsAppReadState : Entity
{
    private WhatsAppReadState() { }
    public Guid OperatorUserId { get; private set; }
    public Guid ContactId { get; private set; }
    public long LastReadSequence { get; private set; }

    public static WhatsAppReadState Create(Guid operatorUserId, Guid contactId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(operatorUserId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(contactId, Guid.Empty);
        return new WhatsAppReadState { OperatorUserId = operatorUserId, ContactId = contactId };
    }

    public void Advance(long sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        LastReadSequence = Math.Max(LastReadSequence, sequence);
    }
}
