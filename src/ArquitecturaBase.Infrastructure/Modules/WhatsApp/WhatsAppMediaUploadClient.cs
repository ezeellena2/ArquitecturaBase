using System.Net.Http.Headers;
using System.Text.Json;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>Uploads private PDF bytes, without exposing their storage key or a public URL to the provider.</summary>
internal sealed class WhatsAppMediaUploadClient(HttpClient httpClient, IOptions<WhatsAppOptions> options) : IWhatsAppMediaUploadClient
{
    public async Task<string?> UploadAsync(WhatsAppOutgoingDocument document, Stream content, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{settings.GraphApiVersion}/{Uri.EscapeDataString(settings.PhoneNumberId ?? string.Empty)}/media");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.AccessToken);
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("whatsapp"), "messaging_product");
        multipart.Add(new StringContent(document.ContentType), "type");
        using var file = new StreamContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(document.ContentType);
        multipart.Add(file, "file", document.Name);
        request.Content = multipart;
        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            // A media id response is tiny. Read a bounded body even when the provider returns malformed content.
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[16 * 1024 + 1]; var count = 0;
            while (count < buffer.Length)
            {
                var read = await body.ReadAsync(buffer.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }
            if (count > 16 * 1024) return null;
            using var json = JsonDocument.Parse(buffer.AsMemory(0, count));
            if (!json.RootElement.TryGetProperty("id", out var value) || value.ValueKind != JsonValueKind.String) return null;
            var id = value.GetString();
            return id is { Length: > 0 and <= 128 } && id.All(char.IsAsciiDigit) ? id : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException
            || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Uploading twice cannot deliver a message twice. No message effect has started yet.
            return null;
        }
    }
}
