using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class AiConnectionChangeRecorder(IAiChangeFingerprint fingerprint, TimeProvider timeProvider, AiAssistantLimits limits)
{
    /// <summary>RC1-A: the most days Home conversations can be kept, from the installation's configuration.</summary>
    public int RetentionCeilingDays => limits.RetentionDays;

    public bool Matches(UpdateAiConnectionRequest request, AiConnectionChange change) =>
        fingerprint.Matches(request, change.KeyId, change.Fingerprint);
    public AiConnectionChange Create(UpdateAiConnectionRequest request, Guid operatorId, AiConnection connection) =>
        AiConnectionChange.Create(request.RequestId, operatorId, fingerprint.Create(request, fingerprint.ActiveKeyId),
            fingerprint.ActiveKeyId, connection, timeProvider.GetUtcNow().UtcDateTime);
    public AiConnectionChangeResponse Response(AiConnectionChange change) => new(change.RequestId,
        AiConnectionResponse.Of(change.Provider, change.Model, change.HasCredential, change.Enabled, change.Revision,
            change.ConversationRetentionDays, RetentionCeilingDays));
}
