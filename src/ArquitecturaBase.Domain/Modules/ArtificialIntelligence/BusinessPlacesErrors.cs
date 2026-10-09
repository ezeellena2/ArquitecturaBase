using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
public static class BusinessPlacesErrors
{
    public static readonly Error NotConfigured = Error.Failure("Ai.Places.NotConfigured", "The business place search is not configured.");
    public static readonly Error ConfigurationInvalid = Error.Failure("Ai.Places.ConfigurationInvalid", "The place provider rejected the server configuration.");
    public static readonly Error Unavailable = Error.Failure("Ai.Places.Unavailable", "The place provider is unavailable.");
    public static readonly Error ResponseInvalid = Error.Failure("Ai.Places.ResponseInvalid", "The place provider response is invalid.");
    public static readonly Error RateLimited = Error.TooManyRequests("Ai.Places.RateLimited", "The place search is rate limited.", new Dictionary<string, object?> { ["retryAfter"] = 60 });
    public static readonly Error Busy = Error.TooManyRequests("Ai.Places.Busy", "The place search capacity is reached.", new Dictionary<string, object?> { ["retryAfter"] = 2 });
}
