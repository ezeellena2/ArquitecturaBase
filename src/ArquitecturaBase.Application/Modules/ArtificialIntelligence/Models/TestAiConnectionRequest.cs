using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record TestAiConnectionRequest(AiProvider Provider, string Model, AiCredentialChange CredentialChange, string? ApiKey, long ExpectedRevision)
{
    public override string ToString() => "TestAiConnectionRequest { credential: [REDACTED] }";
}
