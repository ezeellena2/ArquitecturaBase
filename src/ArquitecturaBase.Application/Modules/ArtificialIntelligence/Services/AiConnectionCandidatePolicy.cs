using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class AiConnectionCandidatePolicy(IAiSecretProtector protector)
{
    public Result<string?> Resolve(AiConnectionRow current, AiProvider provider, AiCredentialChange change, string? apiKey)
    {
        if (change == AiCredentialChange.Keep && current.ProtectedApiKey is not null && provider != current.Provider)
            return AiConnectionErrors.ProviderChanged;
        return change switch
        {
            AiCredentialChange.Keep => Result.Success(current.ProtectedApiKey),
            AiCredentialChange.Remove => Result.Success<string?>(null),
            AiCredentialChange.Replace when !string.IsNullOrWhiteSpace(apiKey) => Result.Success<string?>(protector.Protect(provider, apiKey)),
            _ => AiConnectionErrors.CredentialRequired,
        };
    }
    public Result<string> Reveal(AiProvider provider, string? protectedApiKey)
    {
        if (protectedApiKey is null) return AiConnectionErrors.CredentialRequired;
        var key = protector.Unprotect(provider, protectedApiKey);
        return key is null ? AiConnectionErrors.CredentialUnavailable : Result.Success(key);
    }
    // I7: whether the saved key can be read here, decrypting it locally without calling the provider and discarding it. An unreadable
    // key leaves the assistant unavailable instead of accepting turns that would fail after taking their quota.
    public bool CanReveal(AiConnectionRow connection) =>
        connection.ProtectedApiKey is { } protectedApiKey && protector.Unprotect(connection.Provider, protectedApiKey) is not null;
    public Result<string> RevealCandidate(AiConnectionRow current, AiProvider provider, AiCredentialChange change, string? apiKey)
    {
        if (change == AiCredentialChange.Replace)
            return string.IsNullOrWhiteSpace(apiKey) ? AiConnectionErrors.CredentialRequired : Result.Success(apiKey);
        var candidate = Resolve(current, provider, change, apiKey);
        return candidate.IsFailure ? candidate.Error : Reveal(provider, candidate.Value);
    }
}
