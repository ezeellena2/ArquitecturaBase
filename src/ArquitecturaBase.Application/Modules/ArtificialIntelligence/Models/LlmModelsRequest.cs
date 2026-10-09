using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record LlmModelsRequest(AiProvider Provider, string ApiKey)
{
    public override string ToString() => "LlmModelsRequest { credential: [REDACTED] }";
}
