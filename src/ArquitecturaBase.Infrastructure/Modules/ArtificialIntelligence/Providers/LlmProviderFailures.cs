using System.Net;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;

/// <summary>
/// I6: how the text, audio and document clients classify a failure of the provider, in one place. Never by the provider's body, which
/// is neither read nor logged; nothing here repeats a request.
/// </summary>
internal static class LlmProviderFailures
{
    /// <summary>
    /// By status: the key (401, 403), the account's quota or rate limit (402, 429) and the model or the request (400, 404) each have their
    /// code; the rest of 4xx (and an unfollowed redirect) stays a rejection, and a 5xx is uncertain because generation may have started.
    /// The status travels with the error only for the turn's log.
    /// </summary>
    public static Error FromStatus(HttpStatusCode status)
    {
        var code = (int)status;
        var error = code switch
        {
            401 or 403 => AiAssistantErrors.ProviderCredentialRejected,
            402 or 429 => AiAssistantErrors.ProviderLimited,
            400 or 404 => AiAssistantErrors.ProviderModelRejected,
            >= 500 => AiAssistantErrors.ProviderUncertain,
            _ => AiAssistantErrors.ProviderRejected,
        };
        return AiAssistantErrors.WithProviderStatus(error, code);
    }

    /// <summary>
    /// A failure of the transport. Nothing left when the name did not resolve, the connection was refused, TLS failed or the handler's
    /// connect timeout expired (an <see cref="OperationCanceledException"/> over a <see cref="TimeoutException"/> while the request's own
    /// <paramref name="token"/> is not canceled): unavailable, not uncertain. Anything else may have been sent, and is uncertain.
    /// The timeout of an <see cref="HttpClient"/> throws that same shape after sending: the reading holds because the four assistant clients
    /// have none of their own (an infinite one, checked by the provider contract tests) and their deadline is the request's token.
    /// </summary>
    public static Error FromTransport(Exception exception, CancellationToken token) => exception switch
    {
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError
            or HttpRequestError.SecureConnectionError } => AiAssistantErrors.ProviderUnavailable,
        OperationCanceledException { InnerException: TimeoutException } when !token.IsCancellationRequested => AiAssistantErrors.ProviderUnavailable,
        _ => AiAssistantErrors.ProviderUncertain,
    };
}
