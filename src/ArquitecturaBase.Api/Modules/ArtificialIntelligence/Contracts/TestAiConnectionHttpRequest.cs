using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
public sealed record TestAiConnectionHttpRequest(AiProvider Provider, string Model, AiCredentialChange CredentialChange,
    string? ApiKey, long ExpectedRevision)
{
    public override string ToString() => "TestAiConnectionHttpRequest { credential: [REDACTED] }";
}
