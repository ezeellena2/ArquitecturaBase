namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

public interface IAiConversationProtector
{
    string Protect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string plaintext);
    string? Unprotect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string ciphertext);
}
