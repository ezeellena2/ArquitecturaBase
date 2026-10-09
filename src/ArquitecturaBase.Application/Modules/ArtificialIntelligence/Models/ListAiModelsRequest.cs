using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record ListAiModelsRequest(AiProvider Provider, AiCredentialChange CredentialChange, string? ApiKey, long ExpectedRevision)
{
    public override string ToString() => "ListAiModelsRequest { credential: [REDACTED] }";
}
