using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Options;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppDocumentTransportTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly WhatsAppOptions Settings = new() { PhoneNumberId = "123456", AccessToken = "test-token", GraphApiVersion = "v25.0" };

    [Fact]
    public async Task Private_pdf_upload_uses_the_media_endpoint_and_multipart_bytes_without_storage_urls()
    {
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Json(HttpStatusCode.OK, "{\"id\":\"987654\"}"));
        using var http = new HttpClient(meta) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var client = new WhatsAppMediaUploadClient(http, Options.Create(Settings));
        var document = new WhatsAppOutgoingDocument("private/internal.pdf", "Visita.pdf", "application/pdf", 16, new string('a', 64));
        using var content = new MemoryStream(Encoding.UTF8.GetBytes("%PDF fixed copy"));
        Assert.Equal("987654", await client.UploadAsync(document, content, Ct));
        var request = Assert.Single(meta.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/v25.0/123456/media", request.Uri.AbsolutePath);
        Assert.Equal("Bearer test-token", request.Authorization);
        Assert.Contains("application/pdf", request.Body, StringComparison.Ordinal);
        Assert.Contains("%PDF fixed copy", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(document.StorageKey, request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_document_message_uses_the_uploaded_id_filename_and_caption_without_a_link()
    {
        var meta = new FakeMetaHandler(); using var http = new HttpClient(meta) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var client = new WhatsAppCloudClient(http, Options.Create(Settings));
        var message = new WhatsAppDocumentMessage(PhoneNumber.Create("+5493413654813").Value, "987654", "Visita.pdf", "Tu visita fue aprobada");
        Assert.True((await client.SendAsync(message, Ct)).IsSent);
        var body = JsonNode.Parse(Assert.Single(meta.Requests).Body!)!;
        Assert.Equal("document", body["type"]!.GetValue<string>());
        Assert.Equal("987654", body["document"]!["id"]!.GetValue<string>());
        Assert.Equal(message.Name, body["document"]!["filename"]!.GetValue<string>());
        Assert.Equal(message.Caption, body["document"]!["caption"]!.GetValue<string>());
        Assert.Null(body["document"]!["link"]);
        Assert.DoesNotContain(message.MediaId, message.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(message.Caption, message.ToString(), StringComparison.Ordinal);
    }
}
