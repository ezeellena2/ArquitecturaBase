using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class AiConnectionChangeRecorder(IAiChangeFingerprint fingerprint, TimeProvider timeProvider)
{
    public bool Matches(UpdateAiConnectionRequest request, AiConnectionChange change) =>
        fingerprint.Matches(request, change.KeyId, change.Fingerprint);
    public AiConnectionChange Create(UpdateAiConnectionRequest request, Guid operatorId, AiConnection connection) =>
        AiConnectionChange.Create(request.RequestId, operatorId, fingerprint.Create(request, fingerprint.ActiveKeyId),
            fingerprint.ActiveKeyId, connection, timeProvider.GetUtcNow().UtcDateTime);
    public static AiConnectionChangeResponse Response(AiConnectionChange change) => new(change.RequestId,
        new AiConnectionResponse(change.Provider, change.Model, change.HasCredential, change.Enabled, change.Revision));
}
