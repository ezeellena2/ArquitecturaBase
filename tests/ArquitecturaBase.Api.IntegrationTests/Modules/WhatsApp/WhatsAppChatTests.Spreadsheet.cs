using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// Stage 2 of the assistant authoring: the operator's chat shows a received spreadsheet with its normalized name and its caption as
// the body; the list keeps a generic preview, without the name or the caption.
public sealed partial class WhatsAppChatTests
{
    [Fact]
    public async Task A_received_spreadsheet_shows_its_name_and_caption_in_the_chat_and_a_generic_preview_in_the_list()
    {
        // Letters only, so the search reads it as a name and never as part of a number.
        var name = "Planilla " + string.Concat(Guid.NewGuid().ToString("N").Select(character => (char)('a' + character % 26)));
        var contactId = await factory.InTransactionAsync(async services =>
        {
            var contact = WhatsAppContact.Create(MetaWebhook.UniqueWaId(), null, name, Now);
            var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Spreadsheet,
                "Armame un catálogo con esta planilla", null, Now);
            Assert.True(message.SetSpreadsheetDescriptor("67890", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                new string('a', 64), @"C:\fakepath\clientes.xlsx"));
            message.Classify(WhatsAppMessagePurpose.Business);
            message.SetInboundSequence(1);
            services.GetRequiredService<ApplicationDbContext>().AddRange(contact, message);
            await Task.CompletedTask;
            return contact.Id;
        });
        var actor = await OperatorAsync(WhatsAppPermissions.Read);
        using var client = factory.CreateClient();

        using var thread = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/messages", language: "es", userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var message = Assert.Single((await thread.ReadJsonAsync()).GetProperty("messages").EnumerateArray());
        Assert.Equal(("Spreadsheet", "Armame un catálogo con esta planilla", "clientes.xlsx"),
            (message.GetProperty("kind").GetString(), message.GetProperty("body").GetString(), message.GetProperty("spreadsheetName").GetString()));
        Assert.Equal(JsonValueKind.Null, message.GetProperty("audio").ValueKind);
        await WhatsAppAudioContractCapture.WriteAsync(thread, "spreadsheet-thread", Ct);

        foreach (var (language, preview) in new[] { ("es", "Planilla"), ("en", "Spreadsheet") })
        {
            using var list = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats?search={Uri.EscapeDataString(name)}", language: language,
                userId: actor.ToString());
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var chat = Assert.Single((await list.ReadJsonAsync()).GetProperty("page").GetProperty("items").EnumerateArray());
            Assert.Equal(preview, chat.GetProperty("preview").GetString());
        }
    }
}
