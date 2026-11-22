using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
/// <summary>
/// The connection to save. RC1-A: ConversationRetentionDays, from 1 to the ceiling, is the days to keep Home conversations; without it,
/// the saved ones stay.
/// </summary>
public sealed record UpdateAiConnectionHttpRequest(AiProvider Provider, string Model, AiCredentialChange CredentialChange,
    string? ApiKey, bool Enabled, long ExpectedRevision, Guid RequestId, int? ConversationRetentionDays = null)
{
    public override string ToString() => "UpdateAiConnectionHttpRequest { credential: [REDACTED] }";
}
