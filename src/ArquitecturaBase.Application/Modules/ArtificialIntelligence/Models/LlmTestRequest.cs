using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record LlmTestRequest(AiProvider Provider, string Model, string ApiKey)
{
    public override string ToString() => "LlmTestRequest { credential: [REDACTED] }";
}
