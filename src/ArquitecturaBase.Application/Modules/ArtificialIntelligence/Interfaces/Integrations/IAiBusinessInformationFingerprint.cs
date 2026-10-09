using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
public interface IAiBusinessInformationFingerprint
{
    string ActiveKeyId { get; }
    string Create(UpdateAiBusinessInformationRequest request, string keyId);
    bool Matches(UpdateAiBusinessInformationRequest request, string keyId, string fingerprint);
}
