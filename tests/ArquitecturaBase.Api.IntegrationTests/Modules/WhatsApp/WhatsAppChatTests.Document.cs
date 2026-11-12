using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Task 8 of the documents: the operator's chat shows a received document as a message of its own, with its name (none for a photo), kind
// and pages and its caption as the body, and an unlinked number's with why it was not read (DOC4); the list keeps a generic preview.
public sealed partial class WhatsAppChatTests
{
    [Fact]
    public async Task A_received_document_shows_its_name_kind_and_pages_in_the_chat_and_a_generic_preview_in_the_list()
    {
        // Letters only, so the search reads it as a name and never as part of a number.
        var name = "Documento " + string.Concat(Guid.NewGuid().ToString("N").Select(character => (char)('a' + character % 26)));
        var (contactId, pdfId, photoId) = await factory.InTransactionAsync(async services =>
        {
            var contact = WhatsAppContact.Create(MetaWebhook.UniqueWaId(), null, name, Now);
            var pdf = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ReceivedDocument, "Cargalo en pagos", null, Now);
            Assert.True(pdf.SetDocumentDescriptor("67890", "application/pdf", new string('a', 64), @"C:\fakepath\comprobante.pdf"));
            Assert.True(pdf.StoreDocument($"whatsapp/documents/{pdf.Id:N}.pdf", 2048, new string('b', 64), 2, null).IsSuccess);
            pdf.Classify(WhatsAppMessagePurpose.Business);
            pdf.SetInboundSequence(1);
            var photo = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.ReceivedDocument, null, null, Now.AddSeconds(1));
            Assert.True(photo.SetDocumentDescriptor("67891", "image/jpeg", new string('c', 64), null));
            photo.SetDocumentUnavailable("notLinked");
            photo.Classify(WhatsAppMessagePurpose.Business);
            photo.SetInboundSequence(2);
            services.GetRequiredService<ApplicationDbContext>().AddRange(contact, pdf, photo);
            await Task.CompletedTask;
            return (contact.Id, pdf.Id, photo.Id);
        });
        var actor = await OperatorAsync(WhatsAppPermissions.Read);
        using var client = factory.CreateClient();

        using var thread = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/messages", language: "es", userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var messages = (await thread.ReadJsonAsync()).GetProperty("messages").EnumerateArray().ToArray();
        var shownPdf = messages.Single(message => message.GetProperty("id").GetGuid() == pdfId);
        Assert.Equal(("ReceivedDocument", "Cargalo en pagos"), (shownPdf.GetProperty("kind").GetString(), shownPdf.GetProperty("body").GetString()));
        Assert.Equal("{\"name\":\"comprobante.pdf\",\"kind\":\"pdf\",\"pages\":2}", shownPdf.GetProperty("document").GetRawText());
        Assert.Equal(JsonValueKind.Null, shownPdf.GetProperty("audio").ValueKind);
        Assert.False(shownPdf.TryGetProperty("spreadsheetName", out _));
        var shownPhoto = messages.Single(message => message.GetProperty("id").GetGuid() == photoId);
        // A photo comes without a name: the chat titles it, so the technical one the message keeps («image.jpg») is not sent.
        Assert.Equal("{\"kind\":\"jpeg\",\"pages\":1,\"reasonCode\":\"notLinked\"}", shownPhoto.GetProperty("document").GetRawText());
        await WhatsAppAudioContractCapture.WriteAsync(thread, "document-thread", Ct);

        foreach (var (language, preview) in new[] { ("es", "Documento"), ("en", "Document") })
        {
            using var list = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats?search={Uri.EscapeDataString(name)}", language: language,
                userId: actor.ToString());
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var chat = Assert.Single((await list.ReadJsonAsync()).GetProperty("page").GetProperty("items").EnumerateArray());
            Assert.Equal(preview, chat.GetProperty("preview").GetString());
        }
    }
}
