using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
public interface IAiSecretProtector
{
    string Protect(AiProvider provider, string apiKey);
    string? Unprotect(AiProvider provider, string protectedApiKey);
}
