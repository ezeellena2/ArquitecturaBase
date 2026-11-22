using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
/// <summary>
/// The connection to save. RC1-A: ConversationRetentionDays is the days to keep Home conversations, saved with it; without them, the
/// saved ones stay.
/// </summary>
public sealed record UpdateAiConnectionRequest(AiProvider Provider, string Model, AiCredentialChange CredentialChange, string? ApiKey, bool Enabled, long ExpectedRevision, Guid RequestId,
    int? ConversationRetentionDays = null)
{
    public override string ToString() => "UpdateAiConnectionRequest { credential: [REDACTED] }";
}
