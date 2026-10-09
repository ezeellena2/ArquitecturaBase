using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record UpdateAiConnectionRequest(AiProvider Provider, string Model, AiCredentialChange CredentialChange, string? ApiKey, bool Enabled, long ExpectedRevision, Guid RequestId)
{
    public override string ToString() => "UpdateAiConnectionRequest { credential: [REDACTED] }";
}
