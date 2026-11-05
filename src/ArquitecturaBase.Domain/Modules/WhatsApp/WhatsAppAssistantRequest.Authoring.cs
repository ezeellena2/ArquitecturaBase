namespace ArquitecturaBase.Domain.Modules.WhatsApp;

// IA3-A: «Cambiar algo» leaves the conversation waiting for the change, and the next message continues the proposal's operation.
public sealed partial class WhatsAppAssistantRequest
{
    /// <summary>The operation this interpretation continues; the engine adds its turn to that operation's conversation.</summary>
    public Guid? PreviousRequestId { get; private set; }
    /// <summary>On the reply to «Cambiar algo»: the operation whose proposal the next message changes.</summary>
    public Guid? ChangeRequestId { get; private set; }

    public bool ContinueFrom(Guid previousRequestId)
    {
        if (previousRequestId == Guid.Empty || previousRequestId == RequestId || Kind != WhatsAppAssistantRequestKind.Automatic) return false;
        if (PreviousRequestId.HasValue) return PreviousRequestId == previousRequestId;
        if (State != WhatsAppAssistantRequestState.Pending) return false;
        PreviousRequestId = previousRequestId;
        return true;
    }

    public bool AwaitChange(Guid requestId)
    {
        if (requestId == Guid.Empty || requestId == RequestId || Kind != WhatsAppAssistantRequestKind.Automatic) return false;
        if (ChangeRequestId.HasValue) return ChangeRequestId == requestId;
        if (State != WhatsAppAssistantRequestState.Pending) return false;
        ChangeRequestId = requestId;
        return true;
    }
}
