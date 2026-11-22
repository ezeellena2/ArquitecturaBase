using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
/// <summary>The saved connection; ConversationRetentionDays is the days the administrator chose to keep Home conversations (RC1-A).</summary>
public sealed record AiConnectionRow(AiProvider Provider, string Model, string? ProtectedApiKey, bool Enabled, long Revision,
    int ConversationRetentionDays = AiConnection.InitialConversationRetentionDays)
{
    /// <summary>The days Home conversations are kept: the saved ones, never beyond the installation's ceiling.</summary>
    public int HomeRetentionDays(int ceilingDays) => Math.Clamp(ConversationRetentionDays, 1, ceilingDays);

    public AiConnectionResponse ToResponse(int retentionCeilingDays) => AiConnectionResponse.Of(Provider, Model, ProtectedApiKey is not null, Enabled,
        Revision, ConversationRetentionDays, retentionCeilingDays);
    public override string ToString() => "AiConnectionRow { credential: [REDACTED] }";
}
