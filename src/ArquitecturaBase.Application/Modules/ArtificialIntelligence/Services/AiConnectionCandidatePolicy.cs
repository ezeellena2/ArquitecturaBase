using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class AiConnectionCandidatePolicy(IAiSecretProtector protector)
{
    // I4: the key of a provider this version does not know serves no other one and is never sent: keeping it drops it, as Remove does.
    public Result<string?> Resolve(AiConnectionRow current, AiProvider provider, AiCredentialChange change, string? apiKey)
    {
        if (change == AiCredentialChange.Keep && !AiProviders.IsSupported(current.Provider)) return Result.Success<string?>(null);
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
    // I7: whether the saved key can be read here, probed locally without the provider and without its text. An unreadable key leaves
    // the assistant unavailable instead of accepting turns that would fail after taking their quota. A request probes the same saved
    // key once, however many offers ask (I7, review).
    public bool CanReveal(AiConnectionRow connection)
    {
        if (connection.ProtectedApiKey is not { } protectedApiKey) return false;
        if (probed is { } last && last.Provider == connection.Provider && string.Equals(last.ProtectedApiKey, protectedApiKey, StringComparison.Ordinal))
            return last.Readable;
        var readable = protector.CanUnprotect(connection.Provider, protectedApiKey);
        probed = (connection.Provider, protectedApiKey, readable);
        return readable;
    }
    private (AiProvider Provider, string ProtectedApiKey, bool Readable)? probed;
    public Result<string> RevealCandidate(AiConnectionRow current, AiProvider provider, AiCredentialChange change, string? apiKey)
    {
        if (change == AiCredentialChange.Replace)
            return string.IsNullOrWhiteSpace(apiKey) ? AiConnectionErrors.CredentialRequired : Result.Success(apiKey);
        var candidate = Resolve(current, provider, change, apiKey);
        return candidate.IsFailure ? candidate.Error : Reveal(provider, candidate.Value);
    }
}
