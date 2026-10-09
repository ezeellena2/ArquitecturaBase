using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public static class LlmErrors
{
    public static readonly Error CredentialInvalid = Error.Validation("Ai.Provider.CredentialInvalid", "The provider rejected the credential.");
    public static readonly Error ModelUnavailable = Error.Validation("Ai.Provider.ModelUnavailable", "The model is unavailable or incompatible.");
    public static readonly Error QuotaExceeded = Error.Validation("Ai.Provider.QuotaExceeded", "The provider quota is exhausted.");
    public static readonly Error Unavailable = Error.Failure("Ai.Provider.Unavailable", "The provider is unavailable.");
    public static readonly Error ResponseInvalid = Error.Failure("Ai.Provider.ResponseInvalid", "The provider response is invalid.");
    public static readonly Error ResponseIncomplete = Error.Validation("Ai.Provider.ResponseIncomplete", "The provider did not complete a text response.");
    public static readonly Error TestUncertain = Error.Failure("Ai.Provider.TestUncertain", "The test outcome is unknown and may have been billed.");
    public static Error RateLimited(int? retryAfter = null) => Error.TooManyRequests("Ai.Provider.RateLimited", "The provider is rate limited.",
        retryAfter is { } seconds ? new Dictionary<string, object?> { ["retryAfter"] = seconds } : null);
    public static readonly Error Busy = Error.TooManyRequests("Ai.Provider.Busy", "The local connection test limit is reached.",
        new Dictionary<string, object?> { ["retryAfter"] = 2 });
}
