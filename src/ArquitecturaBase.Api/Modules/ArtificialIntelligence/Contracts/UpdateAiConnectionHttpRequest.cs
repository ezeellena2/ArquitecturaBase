using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
public sealed record UpdateAiConnectionHttpRequest(AiProvider Provider, string Model, AiCredentialChange CredentialChange,
    string? ApiKey, bool Enabled, long ExpectedRevision, Guid RequestId)
{
    public override string ToString() => "UpdateAiConnectionHttpRequest { credential: [REDACTED] }";
}
