using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed partial class WhatsAppFlowClient(HttpClient httpClient, IOptions<WhatsAppOptions> options) : IWhatsAppFlowProvider
{
    private const int MaximumBodyBytes = 1024 * 1024;
    private static readonly string[] Categories = ["OTHER"];
    private static readonly WhatsAppFlowVerificationResponse Unverified = new(false, false, false, false, false);
    public async Task<bool> VerifyPhoneAsync(string wabaId, string phoneNumberId, CancellationToken cancellationToken)
    {
        string? after = null;
        for (var page = 0; page < 10; page++)
        {
            var path = $"{options.Value.GraphApiVersion}/{Uri.EscapeDataString(wabaId)}/phone_numbers?fields=id&limit=100";
            if (after is not null) path += "&after=" + Uri.EscapeDataString(after);
            using var body = await ReadGraphAsync(path, cancellationToken);
            if (body is null || !body.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return false;
            if (data.EnumerateArray().Any(phone => Text(phone, "id") == phoneNumberId)) return true;
            if (!body.RootElement.TryGetProperty("paging", out var paging) || !paging.TryGetProperty("next", out _)
                || !paging.TryGetProperty("cursors", out var cursors)) return false;
            after = Text(cursors, "after"); if (string.IsNullOrWhiteSpace(after)) return false;
        }
        return false;
    }
    public Task<WhatsAppFlowEffectResponse> CreateAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken) =>
        EffectAsync(account, account.WabaId + "/flows", JsonContent.Create(new { name = technicalName, categories = Categories }), true, cancellationToken);
    public Task<WhatsAppFlowEffectResponse> UploadAsync(WhatsAppFlowAccountContext account, string metaFlowId, string json, CancellationToken cancellationToken)
    {
        var content = new MultipartFormDataContent();
        var file = new StringContent(json, Encoding.UTF8, "application/json");
        content.Add(file, "file", "flow.json"); content.Add(new StringContent("flow.json"), "name");
        content.Add(new StringContent("FLOW_JSON"), "asset_type");
        return EffectAsync(account, Uri.EscapeDataString(metaFlowId) + "/assets", content, false, cancellationToken);
    }
    public Task<WhatsAppFlowEffectResponse> PublishAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken) =>
        EffectAsync(account, Uri.EscapeDataString(metaFlowId) + "/publish", new StringContent(""), false, cancellationToken);
    private async Task<WhatsAppFlowEffectResponse> EffectAsync(WhatsAppFlowAccountContext account, string path, HttpContent content, bool creating, CancellationToken ct)
    {
        using (content)
        {
            if (!Matches(account)) return new(false, false, null, [], FlowErrors.AccountMismatch.Code);
            try
            {
                using var request = GraphRequest(HttpMethod.Post, options.Value.GraphApiVersion + "/" + path); request.Content = content;
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if ((int)response.StatusCode is >= 500 or 429) return new(false, true, null, []);
                var bytes = await ReadBytesAsync(response.Content, ct);
                if (!response.IsSuccessStatusCode) return new(false, false, null, []);
                if (bytes is null) return new(false, true, null, []);
                using var document = JsonDocument.Parse(bytes);
                var root = document.RootElement;
                var id = Text(root, "id");
                if (id is not null && (id.Length is < 1 or > 128 || !id.All(char.IsAsciiDigit))) id = null;
                var success = response.IsSuccessStatusCode && (creating ? id is not null : root.TryGetProperty("success", out var flag) && flag.ValueKind == JsonValueKind.True);
                return new(success, response.IsSuccessStatusCode && creating && id is null, id, Pointers(root));
            }
            catch (Exception exception) when (Recoverable(exception, ct)) { return new(false, true, null, []); }
        }
    }
    public async Task<WhatsAppFlowVerificationResponse> VerifyAsync(WhatsAppFlowAccountContext account, string metaFlowId, string technicalName, string expectedHash, CancellationToken cancellationToken)
    {
        if (!Matches(account)) return Unverified;
        const string fields = "id,name,status,validation_errors,json_version,endpoint_uri,data_api_version,whatsapp_business_account,health_status";
        using var metadata = await ReadGraphAsync(options.Value.GraphApiVersion + "/" + Uri.EscapeDataString(metaFlowId) + "?fields=" + Uri.EscapeDataString(fields), cancellationToken);
        if (metadata is null) return Unverified;
        var root = metadata.RootElement;
        var ownerMatches = root.TryGetProperty("whatsapp_business_account", out var owner) && Text(owner, "id") == account.WabaId;
        var identityMatches = Text(root, "id") == metaFlowId && Text(root, "name") == technicalName && ownerMatches
            && Text(root, "json_version") == "7.3" && string.IsNullOrEmpty(Text(root, "endpoint_uri")) && string.IsNullOrEmpty(Text(root, "data_api_version"));
        var validated = root.TryGetProperty("validation_errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() == 0;
        using var phone = await ReadGraphAsync(options.Value.GraphApiVersion + "/" + Uri.EscapeDataString(account.PhoneNumberId)
            + "?fields=id,health_status", cancellationToken);
        var healthy = IsAvailable(root) && phone is not null && Text(phone.RootElement, "id") == account.PhoneNumberId
            && IsAvailable(phone.RootElement);
        var hashMatches = identityMatches && await AssetMatchesAsync(metaFlowId, expectedHash, cancellationToken);
        var status = Text(root, "status");
        return new(status == "PUBLISHED", status == "DRAFT", hashMatches, validated, healthy);
    }
    private async Task<bool> AssetMatchesAsync(string metaFlowId, string expectedHash, CancellationToken ct)
    {
        using var assets = await ReadGraphAsync(options.Value.GraphApiVersion + "/" + Uri.EscapeDataString(metaFlowId) + "/assets", ct);
        if (assets is null || !assets.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return false;
        var files = data.EnumerateArray().Where(asset => Text(asset, "name") == "flow.json" && Text(asset, "asset_type") == "FLOW_JSON").ToArray();
        if (files.Length != 1 || !Uri.TryCreate(Text(files[0], "download_url"), UriKind.Absolute, out var url)
            || url.Scheme != Uri.UriSchemeHttps || url.Port != 443 || url.UserInfo.Length != 0
            || !(url.Host.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase)
                || url.Host.Equals("mmg.whatsapp.net", StringComparison.OrdinalIgnoreCase))) return false;
        try
        {
            // Never attach the Graph bearer to the asset host, and never follow redirects.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return false;
            var bytes = await ReadBytesAsync(response.Content, ct);
            return bytes is not null && Convert.ToHexStringLower(SHA256.HashData(bytes)) == expectedHash;
        }
        catch (Exception exception) when (Recoverable(exception, ct)) { return false; }
    }
    public async Task<WhatsAppFlowVerificationResponse> InspectUnknownCreationAsync(WhatsAppFlowAccountContext account, string technicalName, CancellationToken cancellationToken)
    {
        if (!Matches(account)) return Unverified;
        string? after = null; var count = 0;
        for (var page = 0; page < 10; page++)
        {
            var path = options.Value.GraphApiVersion + "/" + Uri.EscapeDataString(account.WabaId) + "/flows?fields=id,name&limit=100";
            if (after is not null) path += "&after=" + Uri.EscapeDataString(after);
            using var body = await ReadGraphAsync(path, cancellationToken);
            if (body is null || !body.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return Unverified;
            count += data.EnumerateArray().Count(flow => Text(flow, "name") == technicalName);
            if (!body.RootElement.TryGetProperty("paging", out var paging) || !paging.TryGetProperty("next", out _)) return Unverified with { CreationCandidateCount = count };
            if (!paging.TryGetProperty("cursors", out var cursors)) return Unverified;
            after = Text(cursors, "after"); if (string.IsNullOrWhiteSpace(after)) return Unverified;
        }
        return Unverified;
    }
    public async Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(WhatsAppFlowAccountContext account, string metaFlowId, CancellationToken cancellationToken)
    {
        if (!Matches(account)) return FlowErrors.AccountMismatch;
        using var document = await ReadGraphAsync(options.Value.GraphApiVersion + "/" + Uri.EscapeDataString(metaFlowId) + "?fields=preview.invalidate(false)", cancellationToken);
        if (document is null || Text(document.RootElement, "id") != metaFlowId || !document.RootElement.TryGetProperty("preview", out var preview)) return FlowErrors.PreviewUnavailable;
        var value = Text(preview, "preview_url");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps || url.Host != "business.facebook.com"
            || url.Port != 443 || url.UserInfo.Length != 0 || url.AbsolutePath != "/wa/manage/flows/" + metaFlowId + "/preview/") return FlowErrors.PreviewUnavailable;
        var expiry = Text(preview, "expires_at");
        if (expiry is not null && CompactOffsetPattern().IsMatch(expiry)) expiry = expiry.Insert(expiry.Length - 2, ":");
        if (expiry is null || !ExplicitOffsetPattern().IsMatch(expiry)) return FlowErrors.PreviewUnavailable;
        if (!DateTimeOffset.TryParse(expiry, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires)) return FlowErrors.PreviewUnavailable;
        return new StructuredFormPreviewResponse(value!, expires.UtcDateTime);
    }
    private bool Matches(WhatsAppFlowAccountContext account) => account.WabaId == options.Value.WabaId && account.PhoneNumberId == options.Value.PhoneNumberId
        && account.GraphApiVersion == options.Value.GraphApiVersion && account.ConfigurationFingerprint == WhatsAppFlowEnvironment.Fingerprint(options.Value);
    private HttpRequestMessage GraphRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.AccessToken);
        return request;
    }
    private async Task<JsonDocument?> ReadGraphAsync(string path, CancellationToken ct)
    {
        try
        {
            using var request = GraphRequest(HttpMethod.Get, path);
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return null;
            var bytes = await ReadBytesAsync(response.Content, ct);
            return bytes is null ? null : JsonDocument.Parse(bytes);
        }
        catch (Exception exception) when (Recoverable(exception, ct)) { return null; }
    }
    private static async Task<byte[]?> ReadBytesAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaximumBodyBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct); if (count == 0) return output.ToArray();
            if (output.Length + count > MaximumBodyBytes) return null;
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
    }
    private static string? Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool IsAvailable(JsonElement root) => root.TryGetProperty("health_status", out var health)
        && Text(health, "can_send_message") == "AVAILABLE";
    private static bool Recoverable(Exception exception, CancellationToken ct) => exception is HttpRequestException or IOException or JsonException or Polly.ExecutionRejectedException
        || exception is OperationCanceledException && !ct.IsCancellationRequested;
    private static WhatsAppFlowValidationPointer[] Pointers(JsonElement root)
    {
        if (!root.TryGetProperty("validation_errors", out var errors) || errors.ValueKind != JsonValueKind.Array) return [];
        var result = new List<WhatsAppFlowValidationPointer>();
        foreach (var error in errors.EnumerateArray().Take(100))
        {
            if (!error.TryGetProperty("pointers", out var pointers) || pointers.ValueKind != JsonValueKind.Array || pointers.GetArrayLength() == 0)
            { result.Add(new(null, null, "channel")); continue; }
            foreach (var pointer in pointers.EnumerateArray().Take(100))
            {
                var path = Text(pointer, "path"); if (path is null || path.Length > 512) { result.Add(new(null, null, "channel")); continue; }
                var match = PointerPattern().Match(string.Concat(path.Where(character => !char.IsWhiteSpace(character))));
                if (!match.Success) { result.Add(new(null, null, "channel")); continue; }
                var field = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                int? choice = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : null;
                result.Add(new(field, choice, match.Groups["property"].Value));
            }
        }
        return result.ToArray();
    }
    [GeneratedRegex(@"[+-]\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex CompactOffsetPattern();
    [GeneratedRegex(@"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitOffsetPattern();
    [GeneratedRegex(@"^screens\[0\]\.layout\.children\[0\]\.children\[(\d{1,3})\](?:(?:\.data-source|\['data-source'\])\[(\d{1,3})\])?(?:\.(?<property>label|title|helper-text|description|type|required|max-chars|max-length)|\['(?<property>label|title|helper-text|description|type|required|max-chars|max-length)'\])$", RegexOptions.CultureInvariant)]
    private static partial Regex PointerPattern();
}
