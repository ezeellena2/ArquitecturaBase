namespace ArquitecturaBase.Domain.Modules.WhatsApp;

// IA3-A: «Cambiar algo» leaves the conversation waiting for the change, and a question about what to build waits for the answer
// («pregunta y espera»); either way the next message continues an operation.
public sealed partial class WhatsAppAssistantRequest
{
    /// <summary>The operation this interpretation continues; the engine adds its turn to that operation's conversation.</summary>
    public Guid? PreviousRequestId { get; private set; }
    /// <summary>The operation the next message continues: the one whose proposal «Cambiar algo» changes, or this reference's own
    /// operation when it asked what to build.</summary>
    public Guid? ChangeRequestId { get; private set; }

    public bool ContinueFrom(Guid previousRequestId)
    {
        if (previousRequestId == Guid.Empty || previousRequestId == RequestId || Kind != WhatsAppAssistantRequestKind.Automatic) return false;
        if (PreviousRequestId.HasValue) return PreviousRequestId == previousRequestId;
        if (State != WhatsAppAssistantRequestState.Pending) return false;
        PreviousRequestId = previousRequestId;
        return true;
    }

    /// <summary>On a question about what to build: the next message answers it and continues this same operation.</summary>
    public bool AwaitAnswer()
    {
        if (Kind != WhatsAppAssistantRequestKind.Automatic) return false;
        if (ChangeRequestId.HasValue) return ChangeRequestId == RequestId;
        if (State != WhatsAppAssistantRequestState.Pending) return false;
        ChangeRequestId = RequestId;
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
