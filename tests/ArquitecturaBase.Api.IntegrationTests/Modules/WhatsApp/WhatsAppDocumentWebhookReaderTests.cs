using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Task 8 of the documents: a photo Meta declares as a JPEG or a PNG and a document that is not a spreadsheet come as a received document,
/// with its caption and its descriptor; a spreadsheet stays a spreadsheet, and a video, a sticker or another photo stay as media. They do
/// not use the database.
/// </summary>
public sealed class WhatsAppDocumentWebhookReaderTests
{
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";
    private const string MetaHash = "JTF3Dq2S5uMyJnl2OZw6a0VvbQuVWb0uYUCb+KeBP4U=";
    private const string HexHash = "2531770ead92e6e332267976399c3a6b456f6d0b9559bd2e61409bf8a7813f85";
    private static readonly DateTime SentAtUtc = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public void A_photo_is_a_received_document_with_its_caption_and_its_descriptor_without_a_name(string type)
    {
        var message = Read(Photo(type, "Cargalo en pagos"));

        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, "Cargalo en pagos"), (message.Kind, message.Body));
        Assert.Equal(new WhatsAppWebhookDocumentDescriptor("1003383421387256", type, HexHash, null), message.Document);
        Assert.Null(message.Spreadsheet);
    }

    [Theory]
    [InlineData("comprobante.pdf", "application/pdf")]
    [InlineData("informe.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("notas.doc", "application/msword")]
    [InlineData("archivo.zip", "application/zip")]
    [InlineData(null, "application/octet-stream")]
    public void A_document_that_is_not_a_spreadsheet_is_a_received_document_with_its_name(string? name, string type)
    {
        var message = Read(MetaWebhook.Document("wamid.document", WaId, Bsuid, SentAtUtc, name, type, "Mirá este archivo", sha256: MetaHash));

        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, "Mirá este archivo"), (message.Kind, message.Body));
        Assert.Equal(new WhatsAppWebhookDocumentDescriptor("1003383421387256", type, HexHash, name), message.Document);
    }

    [Fact]
    public void A_document_without_a_caption_has_no_body()
    {
        var message = Read(MetaWebhook.Document("wamid.document", WaId, Bsuid, SentAtUtc, "comprobante.pdf", "application/pdf"));

        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, (string?)null), (message.Kind, message.Body));
    }

    [Fact]
    public void A_spreadsheet_stays_a_spreadsheet()
    {
        var message = Read(MetaWebhook.Document("wamid.xlsx", WaId, Bsuid, SentAtUtc, "clientes.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Armame un catálogo"));

        Assert.Equal(WhatsAppMessageKind.Spreadsheet, message.Kind);
        Assert.NotNull(message.Spreadsheet);
        Assert.Null(message.Document);
    }

    [Theory]
    [InlineData("video", "video/mp4")]
    [InlineData("sticker", "image/webp")]
    [InlineData("image", "image/webp")]
    [InlineData("image", null)]
    public void A_video_a_sticker_or_another_photo_stays_as_media(string type, string? mimeType)
    {
        var inbound = MetaWebhook.Text("wamid.media", WaId, Bsuid, SentAtUtc, "x");
        inbound.Remove("text");
        inbound["type"] = type;
        var media = new JsonObject { ["id"] = "1003383421387256", ["sha256"] = MetaHash, ["caption"] = "Mirá" };
        if (mimeType is not null) media["mime_type"] = mimeType;
        inbound[type] = media;

        var message = Read(inbound);

        Assert.Equal((WhatsAppMessageKind.Media, (string?)null, (WhatsAppWebhookDocumentDescriptor?)null), (message.Kind, message.Body, message.Document));
    }

    [Theory]
    [InlineData("id")]
    [InlineData("sha256")]
    [InlineData("malformedHash")]
    public void A_document_whose_file_cannot_be_downloaded_keeps_its_caption_and_its_name(string broken)
    {
        var inbound = MetaWebhook.Document("wamid.broken", WaId, Bsuid, SentAtUtc, "comprobante.pdf", "application/pdf", "Este es el pago");
        var document = (JsonObject)inbound["document"]!;
        if (broken == "malformedHash") document["sha256"] = "not-a-hash";
        else document.Remove(broken);

        var message = Read(inbound);

        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, "Este es el pago", "comprobante.pdf"), (message.Kind, message.Body, message.Document!.FileName));
        Assert.True(message.Document.MediaId is null || message.Document.Sha256 is null);
    }

    private static JsonObject Photo(string type, string? caption)
    {
        var inbound = MetaWebhook.Image("wamid.photo", WaId, Bsuid, SentAtUtc);
        var image = (JsonObject)inbound["image"]!;
        image["mime_type"] = type;
        if (caption is null) image.Remove("caption");
        else image["caption"] = caption;
        return inbound;
    }

    private static WhatsAppWebhookMessage Read(JsonObject inbound)
    {
        var reader = new WhatsAppWebhookReader(Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "fictional-key" }),
            NullLogger<WhatsAppWebhookReader>.Instance);
        return Assert.Single(reader.Read(MetaWebhook.Build(contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")], messages: [inbound])).Messages);
    }
}
