using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record AiConnectionResponse(AiProvider Provider, string Model, bool HasCredential, bool Enabled, long Revision)
{
    private static IReadOnlyCollection<AiProvider> SupportedProviders { get; } = [AiProvider.OpenAI, AiProvider.Anthropic];
    public IReadOnlyCollection<AiProvider> Providers { get; } = SupportedProviders;
}
