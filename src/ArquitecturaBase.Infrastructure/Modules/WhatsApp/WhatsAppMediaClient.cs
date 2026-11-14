using System.Net.Http.Headers;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed partial class WhatsAppMediaClient(HttpClient http, IOptions<WhatsAppOptions> options, TimeProvider timeProvider,
    ILogger<WhatsAppMediaClient> logger) : IWhatsAppMediaClient
{

    public async Task<Result<WhatsAppMediaContent>> DownloadAsync(string mediaId, string contentType, string sha256, long maximumBytes, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
        if (string.IsNullOrEmpty(options.Value.PhoneNumberId) || string.IsNullOrEmpty(options.Value.AccessToken)
            || string.IsNullOrEmpty(mediaId) || mediaId.Length > 128 || mediaId.Any(character => character is < '0' or > '9')) return FileErrors.Unavailable;
        // The size Meta declares is checked before downloading, and the bytes read are bounded by the same limit.
        var maximum = (int)Math.Min(maximumBytes, WhatsAppMediaContent.MaximumBytes);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30), timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var ct = linked.Token;
        try
        {
            var path = options.Value.GraphApiVersion + "/" + mediaId + "?phone_number_id=" + Uri.EscapeDataString(options.Value.PhoneNumberId);
            using var metadataRequest = Request(new Uri(http.BaseAddress!, path));
            using var metadataResponse = await http.SendAsync(metadataRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!metadataResponse.IsSuccessStatusCode) return Unavailable(metadataResponse.StatusCode, "metadata");
            var metadataBytes = await ReadAsync(metadataResponse.Content, 8192, ct);
            if (metadataBytes is null) return FileErrors.Unavailable;
            using var metadata = JsonDocument.Parse(metadataBytes);
            var root = metadata.RootElement;
            if (Text(root, "id") != mediaId || !SameContentType(Text(root, "mime_type"), contentType) || Text(root, "sha256") is not { } expectedHash)
                return FileErrors.InvalidMetadata;
            if (NormalizeHash(expectedHash) is not { } hash || hash != NormalizeHash(sha256)) return FileErrors.HashMismatch;
            if (!root.TryGetProperty("file_size", out var sizeValue) || sizeValue.ValueKind != JsonValueKind.Number || !sizeValue.TryGetInt64(out var size) || size <= 0) return FileErrors.InvalidMetadata;
            if (size > maximum) return FileErrors.TooLarge;
            if (!Uri.TryCreate(Text(root, "url"), UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
                || url.Port != 443 || url.UserInfo.Length != 0 || url.Fragment.Length != 0
                || url.Host is not ("lookaside.fbsbx.com" or "mmg.whatsapp.net")) return FileErrors.Unavailable;
            // Bearer is only attached after validating the provider URL. Redirects are disabled in registration.
            using var contentRequest = Request(url);
            using var contentResponse = await http.SendAsync(contentRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!contentResponse.IsSuccessStatusCode) return Unavailable(contentResponse.StatusCode, "content");
            var bytes = await ReadAsync(contentResponse.Content, maximum, ct);
            if (bytes is null) return FileErrors.TooLarge;
            if (bytes.LongLength != size) return FileErrors.InvalidMetadata;
            return new WhatsAppMediaContent(bytes);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return FileErrors.Unavailable;
        }
    }
    // H11: a 401 is the token and a 403 a permission, apart in the log and with the request Meta refused (its metadata in Graph or
    // the content in the media host); the file stays unavailable and keeps retrying (the warning to /health is task 16).
    private Error Unavailable(System.Net.HttpStatusCode status, string step)
    {
        if (status == System.Net.HttpStatusCode.Unauthorized) LogInvalidToken(logger, step);
        else if (status == System.Net.HttpStatusCode.Forbidden) LogMissingPermission(logger, step);
        return FileErrors.Unavailable;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A WhatsApp media download ({Step}) was refused because the access token is not valid (401)")]
    private static partial void LogInvalidToken(ILogger logger, string step);

    [LoggerMessage(Level = LogLevel.Error, Message = "A WhatsApp media download ({Step}) was refused because the token lacks a permission (403)")]
    private static partial void LogMissingPermission(ILogger logger, string step);

    private HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
        return request;
    }
    private static async Task<byte[]?> ReadAsync(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) return null;
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await input.ReadAsync(buffer, ct);
            if (count == 0) return output.ToArray();
            if (output.Length + count > maximum) return null;
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }
    private static bool SameContentType(string? actual,string expected)
    {
        static bool Opus(string? value)=>MediaTypeHeaderValue.TryParse(value,out var type)
            && string.Equals(type.MediaType,"audio/ogg",StringComparison.OrdinalIgnoreCase)
            && type.Parameters.All(parameter=>parameter.Name.Equals("codecs",StringComparison.OrdinalIgnoreCase)
                && string.Equals(parameter.Value?.Trim('"'),"opus",StringComparison.OrdinalIgnoreCase));
        return actual==expected || Opus(actual) && Opus(expected);
    }
    private static string? Text(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? NormalizeHash(string value)
    {
        try
        {
            var bytes = value.Length == 64 ? Convert.FromHexString(value) : Convert.FromBase64String(value);
            return bytes.Length == 32 ? Convert.ToHexStringLower(bytes) : null;
        }
        catch (FormatException) { return null; }
    }
}
