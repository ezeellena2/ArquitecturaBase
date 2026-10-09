using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
public interface IAiChangeFingerprint
{
    string ActiveKeyId { get; }
    string Create(UpdateAiConnectionRequest request, string keyId);
    bool Matches(UpdateAiConnectionRequest request, string keyId, string fingerprint);
}
