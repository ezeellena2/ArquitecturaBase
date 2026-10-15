using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppMediaClientTests
{
    [Fact]
    public async Task Media_is_downloaded_from_the_verified_host_with_the_expected_size_and_account()
    {
        var bytes = "%PDF-1.7\n%%EOF"u8.ToArray();
        var hash = Convert.ToBase64String(SHA256.HashData(bytes));
        using var handler = new StubFlowHttpHandler(request => request.RequestUri!.Host == "graph.facebook.com"
            ? Metadata("https://lookaside.fbsbx.com/whatsapp_business/attachments/?test=synthetic", bytes.Length, hash)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var client = new WhatsAppMediaClient(http, Options.Create(new WhatsAppOptions { PhoneNumberId = "111", AccessToken = "test-token", GraphApiVersion = "v25.0" }), TimeProvider.System);
        var result = await client.DownloadAsync("123", "application/pdf", hash, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(bytes, result.Value.Content);
        Assert.Contains("phone_number_id=111", handler.Requests[0].RequestUri!.Query);
        Assert.Equal(2, handler.Requests.Count);
        Assert.DoesNotContain("test-token", result.ToString());
    }
    [Theory]
    [InlineData("https://lookaside.fbsbx.com.evil.test/file")]
    [InlineData("http://lookaside.fbsbx.com/file")]
    [InlineData("https://user:secret@lookaside.fbsbx.com/file")]
    [InlineData("https://lookaside.fbsbx.com:444/file")]
    public async Task An_untrusted_download_url_never_receives_a_request_or_credentials(string url)
    {
        var hash = Convert.ToBase64String(SHA256.HashData([1, 2]));
        using var handler = new StubFlowHttpHandler(_ => Metadata(url, 2, hash));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var client = new WhatsAppMediaClient(http, Options.Create(new WhatsAppOptions { PhoneNumberId = "111", AccessToken = "test-token" }), TimeProvider.System);
        Assert.Equal(FileErrors.Unavailable, (await client.DownloadAsync("123", "application/pdf", hash, TestContext.Current.CancellationToken)).Error);
        Assert.Single(handler.Requests);
    }
    private static HttpResponseMessage Metadata(string url, int size, string hash) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { id = "123", url, file_size = size, mime_type = "application/pdf", sha256 = hash })) };
}
