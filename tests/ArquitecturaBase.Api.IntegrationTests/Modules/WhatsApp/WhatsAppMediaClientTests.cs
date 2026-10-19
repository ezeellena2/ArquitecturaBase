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
    [Theory]
    [InlineData("\"123\"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("true")]
    public async Task A_non_numeric_media_size_is_rejected_without_downloading(string sizeJson)
    {
        var hash = Convert.ToBase64String(SHA256.HashData([1, 2]));
        using var handler = new StubFlowHttpHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { id = "123", mime_type = "application/pdf", sha256 = hash }).TrimEnd('}') + ",\"file_size\":" + sizeJson + "}")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var client = new WhatsAppMediaClient(http, Options.Create(new WhatsAppOptions { PhoneNumberId = "111", AccessToken = "test-token" }), TimeProvider.System);
        Assert.Equal(FileErrors.InvalidMetadata, (await client.DownloadAsync("123", "application/pdf", hash, TestContext.Current.CancellationToken)).Error);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("audio/ogg", "audio/ogg; codecs=opus", true)]
    [InlineData("audio/ogg; codecs=opus", "audio/ogg", true)]
    [InlineData("audio/ogg; codecs=vorbis", "audio/ogg", false)]
    [InlineData("audio/mpeg", "audio/ogg", false)]
    public async Task Audio_metadata_accepts_only_ogg_opus_parameters_without_changing_other_media(string providerMime,string expectedMime,bool accepted)
    {
        byte[] bytes=[1,2];var hash=Convert.ToBase64String(SHA256.HashData(bytes));
        using var handler=new StubFlowHttpHandler(request=>request.RequestUri!.Host=="graph.facebook.com"
            ?Metadata("https://lookaside.fbsbx.com/whatsapp_business/attachments/test",bytes.Length,hash,providerMime)
            :new(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});
        using var http=new HttpClient(handler){BaseAddress=new Uri("https://graph.facebook.com/")};
        var client=new WhatsAppMediaClient(http,Options.Create(new WhatsAppOptions{PhoneNumberId="111",AccessToken="test-token"}),TimeProvider.System);
        var result=await client.DownloadAsync("123",expectedMime,hash,TestContext.Current.CancellationToken);
        Assert.Equal(accepted,result.IsSuccess);Assert.Equal(accepted?2:1,handler.Requests.Count);
    }
    [Theory]
    [InlineData(25_000_000, true)]
    [InlineData(25_000_001, true)]
    [InlineData(25 * 1024 * 1024, true)]
    [InlineData(25 * 1024 * 1024 + 1, false)]
    public async Task Flow_files_use_the_binary_limit_independently_of_private_audio_storage(int size, bool accepted)
    {
        var bytes = new byte[size];
        var hash = Convert.ToBase64String(SHA256.HashData(bytes));
        using var handler = new StubFlowHttpHandler(request => request.RequestUri!.Host == "graph.facebook.com"
            ? Metadata("https://lookaside.fbsbx.com/whatsapp_business/attachments/test", size, hash)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var client = new WhatsAppMediaClient(http, Options.Create(new WhatsAppOptions { PhoneNumberId = "111", AccessToken = "test-token" }), TimeProvider.System);
        var result = await client.DownloadAsync("123", "application/pdf", hash, TestContext.Current.CancellationToken);
        Assert.Equal(accepted, result.IsSuccess);
        if (accepted) Assert.Equal(bytes, result.Value.Content);
        else Assert.Equal(FileErrors.TooLarge, result.Error);
        Assert.Equal(accepted ? 2 : 1, handler.Requests.Count);
    }

    [Fact]
    public async Task A_download_above_the_flow_file_limit_is_rejected_even_when_its_metadata_fits()
    {
        var bytes = new byte[25 * 1024 * 1024 + 1];
        var hash = Convert.ToBase64String(SHA256.HashData(bytes));
        using var handler = new StubFlowHttpHandler(request => request.RequestUri!.Host == "graph.facebook.com"
            ? Metadata("https://lookaside.fbsbx.com/whatsapp_business/attachments/test", bytes.Length - 1, hash)
            : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) { Headers = { ContentLength = bytes.Length - 1 } } });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/") };
        var client = new WhatsAppMediaClient(http, Options.Create(new WhatsAppOptions { PhoneNumberId = "111", AccessToken = "test-token" }), TimeProvider.System);
        var result = await client.DownloadAsync("123", "application/pdf", hash, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FileErrors.TooLarge, result.Error);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static HttpResponseMessage Metadata(string url, int size, string hash, string mime = "application/pdf") => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { id = "123", url, file_size = size, mime_type = mime, sha256 = hash })) };
}
