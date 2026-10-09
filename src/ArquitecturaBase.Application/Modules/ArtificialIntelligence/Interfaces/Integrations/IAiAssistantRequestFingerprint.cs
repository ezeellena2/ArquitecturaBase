using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

public interface IAiAssistantRequestFingerprint
{
    string ActiveKeyId { get; }
    string Create(Guid ownerUserId, SubmitAiAssistantTurnRequest request, string keyId);
    bool Matches(Guid ownerUserId, SubmitAiAssistantTurnRequest request, string keyId, string fingerprint);
}
