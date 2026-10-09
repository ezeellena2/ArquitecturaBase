using System.Net;
using System.Text.Json;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
internal sealed class LlmHttpTransport(IOptions<AiTransportOptions> options, TimeProvider timeProvider)
{
    public const string TestPrompt = "Reply with the single word OK.";
    public int MaxOutputTokens => options.Value.MaxOutputTokens;
    public async Task<Result<T>> SendAsync<T>(HttpClient client, HttpRequestMessage request,
        Func<JsonElement, Result<T>> parse, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return LlmErrors.CredentialInvalid;
            if (response.StatusCode == HttpStatusCode.NotFound) return LlmErrors.ModelUnavailable;
            if (response.StatusCode == HttpStatusCode.PaymentRequired) return LlmErrors.QuotaExceeded;
            if ((int)response.StatusCode >= 500) return LlmErrors.Unavailable;
            if (response.Content.Headers.ContentLength > options.Value.MaxResponseBytes) return LlmErrors.ResponseInvalid;
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (body.Length + read > options.Value.MaxResponseBytes) return LlmErrors.ResponseInvalid;
                body.Write(buffer, 0, read);
            }
            JsonDocument? document = null;
            try { document = JsonDocument.Parse(body.GetBuffer().AsMemory(0, checked((int)body.Length)), new JsonDocumentOptions { MaxDepth = 20 }); }
            catch (JsonException) when (!response.IsSuccessStatusCode) { }
            using (document)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var root = document?.RootElement;
                    var code = root is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty("error", out var error)
                        ? Text(error, "code") : null;
                    if (code == "insufficient_quota") return LlmErrors.QuotaExceeded;
                    var wait = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - timeProvider.GetUtcNow());
                    return LlmErrors.RateLimited(wait is { } delay ? Math.Clamp((int)Math.Ceiling(delay.TotalSeconds), 1, 86400) : null);
                }
                if (response.StatusCode == HttpStatusCode.BadRequest)
                {
                    var root = document?.RootElement;
                    var message = root is { ValueKind: JsonValueKind.Object } element && element.TryGetProperty("error", out var error)
                        ? Text(error, "message") : null;
                    return message?.StartsWith("Your credit balance is too low", StringComparison.Ordinal) == true ? LlmErrors.QuotaExceeded : LlmErrors.ModelUnavailable;
                }
                if (!response.IsSuccessStatusCode) return LlmErrors.Unavailable;
                if (document?.RootElement.ValueKind != JsonValueKind.Object) return LlmErrors.ResponseInvalid;
                return parse(document.RootElement);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return request.Method == HttpMethod.Post ? LlmErrors.TestUncertain : LlmErrors.Unavailable; }
        catch (HttpRequestException) { return request.Method == HttpMethod.Post ? LlmErrors.TestUncertain : LlmErrors.Unavailable; }
        catch (IOException) { return request.Method == HttpMethod.Post ? LlmErrors.TestUncertain : LlmErrors.Unavailable; }
        catch (JsonException) { return LlmErrors.ResponseInvalid; }
    }
    public static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static int? Tokens(JsonElement root, string name) => root.TryGetProperty("usage", out var usage)
        && usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= 0 ? number : null;
}
