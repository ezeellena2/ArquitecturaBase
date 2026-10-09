using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
public sealed record ListAiModelsHttpRequest(AiProvider Provider, AiCredentialChange CredentialChange, string? ApiKey, long ExpectedRevision)
{
    public override string ToString() => "ListAiModelsHttpRequest { credential: [REDACTED] }";
}
