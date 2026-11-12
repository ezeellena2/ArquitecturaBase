using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// Task 8 of the documents: the operator's chat shows a received document as a message of its own, with its name, its kind and its pages,
// and its caption as the body; an unlinked number's says why it was not read (DOC4). The list keeps a generic preview.
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    private static WhatsAppMessageRow DocumentRow(string? caption, WhatsAppReceivedDocumentMessageRow? document) => new(Guid.NewGuid(),
        WhatsAppMessageDirection.Inbound, WhatsAppMessageKind.ReceivedDocument, caption, Now, 1, null, null, null, WhatsAppMessagePurpose.Business) { Document = document };

    [Theory]
    [InlineData("application/pdf", "whatsapp/documents/00000000000000000000000000000001.pdf", 3, "pdf", 3)]
    [InlineData("image/jpeg", "whatsapp/documents/00000000000000000000000000000001.png", null, "png", 1)]
    [InlineData("image/jpeg", null, null, "jpeg", 1)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", null, null, "docx", null)]
    [InlineData("application/zip", null, null, null, null)]
    public async Task A_document_shows_its_name_kind_and_pages_and_its_caption_as_the_body(string contentType, string? storageKey, int? pages,
        string? kind, int? shownPages)
    {
        var host = new Host();
        host.Reader.Audio = DocumentRow("Cargalo en pagos", new("comprobante.pdf", contentType, storageKey, pages, WhatsAppDocumentState.Stored, null));

        var message = Assert.Single((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);

        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, "Cargalo en pagos"), (message.Kind, message.Body));
        Assert.Equal(new WhatsAppReceivedDocumentResponse("comprobante.pdf") { Kind = kind, Pages = shownPages }, message.Document);
        Assert.Null(message.SpreadsheetName);
        Assert.Null(message.Document!.ReasonCode);
    }

    [Fact]
    public async Task An_unlinked_number_document_says_why_it_was_not_read_and_its_body_keeps_only_present_fields()
    {
        var host = new Host();
        host.Reader.Audio = DocumentRow(null, new("image.jpg", "image/jpeg", null, null, WhatsAppDocumentState.Unavailable, "notLinked"));

        var message = Assert.Single((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);

        Assert.Equal("notLinked", message.Document!.ReasonCode);
        Assert.Contains("\"document\":{\"kind\":\"jpeg\",\"pages\":1,\"reasonCode\":\"notLinked\"}",
            JsonSerializer.Serialize(message, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    // Meta's photo has no name: the technical one the message keeps («image.jpg») never reaches the chat, which titles it «Foto». The kind
    // decides, as its owner detected it: a PDF or a Word that came as a photo keeps that name, and one sent as a document keeps the person's.
    [Theory]
    [InlineData("image.jpg", "image/jpeg", null, "jpeg", null)]
    [InlineData("image.png", "image/png", null, "png", null)]
    [InlineData("image.jpg", "image/jpeg", "whatsapp/documents/00000000000000000000000000000001.jpg", "jpeg", null)]
    [InlineData("image.jpg", "image/jpeg", "whatsapp/documents/00000000000000000000000000000001.pdf", "pdf", "image.jpg")]
    [InlineData("image.jpg", "image/jpeg", "whatsapp/documents/00000000000000000000000000000001.docx", "docx", "image.jpg")]
    [InlineData("recibo.jpg", "image/jpeg", null, "jpeg", "recibo.jpg")]
    [InlineData("document.pdf", "application/pdf", null, "pdf", "document.pdf")]
    public async Task A_photo_without_a_name_reaches_the_chat_without_one(string fileName, string contentType, string? storageKey, string kind, string? shown)
    {
        var host = new Host();
        host.Reader.Audio = DocumentRow(null, new(fileName, contentType, storageKey, null, WhatsAppDocumentState.Stored, null));

        var message = Assert.Single((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);

        Assert.Equal((shown, kind), (message.Document!.Name, message.Document.Kind));
        Assert.Equal(shown is not null, JsonSerializer.Serialize(message, JsonSerializerOptions.Web).Contains("\"name\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WhatsAppDocumentState.Unavailable, "notAllowed")]
    [InlineData(WhatsAppDocumentState.Unavailable, "WhatsApp.Assistant.Unavailable")]
    [InlineData(WhatsAppDocumentState.Rejected, "protected")]
    public async Task No_other_reason_reaches_the_chat(WhatsAppDocumentState state, string reason)
    {
        var host = new Host();
        host.Reader.Audio = DocumentRow(null, new("comprobante.pdf", "application/pdf", null, null, state, reason));

        var message = Assert.Single((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);

        Assert.Null(message.Document!.ReasonCode);
        Assert.DoesNotContain("reasonCode", JsonSerializer.Serialize(message, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    // Retention takes the name with the body: the message keeps its kind and no document.
    [Fact]
    public async Task A_document_whose_descriptor_expired_and_any_other_message_carry_no_document()
    {
        var host = new Host();
        host.Reader.Audio = DocumentRow(null, new(null, null, null, null, WhatsAppDocumentState.Read, null));
        var expired = Assert.Single((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);
        var other = Assert.Single((await new Host().Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);

        Assert.Equal((WhatsAppMessageKind.ReceivedDocument, (WhatsAppReceivedDocumentResponse?)null), (expired.Kind, expired.Document));
        Assert.Null(other.Document);
        Assert.DoesNotContain("\"document\"", JsonSerializer.Serialize(other, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("es", "Documento")]
    [InlineData("en", "Document")]
    public async Task Document_list_preview_is_a_generic_label_without_its_name_or_caption(string culture, string preview)
    {
        using var scope = new CultureScope(culture);
        var host = new Host();
        host.Reader.PreviewKind = WhatsAppMessageKind.ReceivedDocument;

        var result = await host.Service.ListAsync(new(), Guid.NewGuid(), Ct);

        Assert.Equal(preview, Assert.Single(result.Value.Page.Items).Preview);
    }
}
