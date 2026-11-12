using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// W3 (task 22): what the person shares arrives through the signed webhook with Meta's bodies. A contact is a message of its own whose chat
// shows the name and the phones of its first card as they came (none without a name or without a phone), and retention takes the card with
// the body; the list keeps a generic preview. A shared place keeps its address in the history, never in the chat.
public sealed partial class WhatsAppChatTests
{
    private const string AnaCard = """
        {"name":{"formatted_name":"Ana Gómez","first_name":"Ana","last_name":"Gómez"},"org":{"company":"Distribuidora de prueba"},
         "phones":[{"phone":"+54 9 341 365-4813","wa_id":"5493413654813","type":"CELL"},{"phone":"+54 341 444-5566","type":"WORK"}]}
        """;

    [Fact]
    public async Task A_shared_contact_shows_its_first_card_in_the_chat_and_a_generic_preview_in_the_list()
    {
        // Letters only, so the search reads it as a name and never as part of a number.
        var name = "Contacto " + string.Concat(Guid.NewGuid().ToString("N").Select(character => (char)('a' + character % 26)));
        var (waId, bsuid) = (MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid());
        await PostWebhookAsync(MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, bsuid, name)], messages:
        [
            // An old card that the retention clears, then three as Meta sends them: with two phones (and a second card that is not kept),
            // without a name and without a phone.
            ContactsMessage(waId, bsuid, Now.AddDays(-91), """{"name":{"formatted_name":"Persona vieja"},"phones":[{"phone":"+54 341 400-0000","type":"CELL"}]}"""),
            ContactsMessage(waId, bsuid, Now, AnaCard, """{"name":{"formatted_name":"Bruno Díaz"},"phones":[{"phone":"+54 351 555-0000","type":"HOME"}]}"""),
            ContactsMessage(waId, bsuid, Now.AddSeconds(1), """{"phones":[{"phone":"+54 9 341 365-4813","wa_id":"5493413654813","type":"CELL"}]}"""),
            ContactsMessage(waId, bsuid, Now.AddSeconds(2), """{"name":{"formatted_name":"Carla Pérez"},"emails":[{"email":"carla@example.com","type":"WORK"}]}"""),
        ]));
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IWhatsAppMessageRetentionRepository>().ClearExpiredBodiesAsync(Now.AddDays(-90), Ct));
        var contactId = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppContact>().Where(contact => contact.WaId == waId).Select(contact => contact.Id).SingleAsync(Ct));
        var actor = await OperatorAsync(WhatsAppPermissions.Read);
        using var client = factory.CreateClient();

        using var thread = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{contactId}/messages", language: "es", userId: actor.ToString());

        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var messages = (await thread.ReadJsonAsync()).GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(4, messages.Length);
        Assert.All(messages, message => Assert.Equal(("Contact", JsonValueKind.Null), (message.GetProperty("kind").GetString(), message.GetProperty("body").ValueKind)));
        // Cleared by the retention: the whole contact is absent, never null.
        Assert.False(messages[0].TryGetProperty("contact", out _));
        AssertCard(messages[1], "Ana Gómez", "+54 9 341 365-4813", "+54 341 444-5566");
        AssertCard(messages[2], null, "+54 9 341 365-4813");
        AssertCard(messages[3], "Carla Pérez");
        Assert.All(messages, message => Assert.False(message.TryGetProperty("location", out _)));
        await WhatsAppAudioContractCapture.WriteAsync(thread, "contact-thread", Ct);

        foreach (var (language, preview) in new[] { ("es", "Contacto"), ("en", "Contact") })
        {
            using var list = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats?search={Uri.EscapeDataString(name)}", language: language,
                userId: actor.ToString());
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var chat = Assert.Single((await list.ReadJsonAsync()).GetProperty("page").GetProperty("items").EnumerateArray());
            Assert.Equal(preview, chat.GetProperty("preview").GetString());
        }
    }

    [Fact]
    public async Task A_shared_place_keeps_its_address_in_the_history_and_the_chat_shows_only_its_point()
    {
        var (waId, bsuid) = (MetaWebhook.UniqueWaId(), MetaWebhook.UniqueBsuid());
        var waMessageId = MetaWebhook.UniqueWaMessageId();
        await PostWebhookAsync(MetaWebhook.Build(contacts: [MetaWebhook.Contact(waId, bsuid, "Cliente con un lugar")], messages:
            [MetaWebhook.Location(waMessageId, waId, bsuid, Now, JsonValue.Create(-32.947102m), JsonValue.Create(-60.638955m), "Hospital de prueba", "Bv. Oroño 1234, S2000 Rosario")]));
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppMessage>().AsNoTracking().SingleAsync(message => message.WaMessageId == waMessageId, Ct));
        var actor = await OperatorAsync(WhatsAppPermissions.Read);
        using var client = factory.CreateClient();

        using var thread = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{stored.ContactId}/messages", userId: actor.ToString());

        Assert.Equal((WhatsAppMessageKind.Location, (decimal?)-32.947102m, "Bv. Oroño 1234, S2000 Rosario"), (stored.Kind, stored.Latitude, stored.LocationAddress));
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var location = Assert.Single((await thread.ReadJsonAsync()).GetProperty("messages").EnumerateArray()).GetProperty("location");
        // The front reads the point as a closed object: the address stays out of it.
        Assert.Equal(["latitude", "longitude"], location.EnumerateObject().Select(property => property.Name));
    }

    private static JsonObject ContactsMessage(string waId, string bsuid, DateTime sentAtUtc, params string[] cards) => new()
    {
        ["from"] = waId, ["from_user_id"] = bsuid, ["id"] = MetaWebhook.UniqueWaMessageId(), ["timestamp"] = MetaWebhook.Timestamp(sentAtUtc),
        ["type"] = "contacts", ["contacts"] = new JsonArray([.. cards.Select(card => JsonNode.Parse(card))]),
    };

    // The card as the front's closed guard reads it: exactly `name` (null or text) and `phones`.
    private static void AssertCard(JsonElement message, string? name, params string[] phones)
    {
        var contact = message.GetProperty("contact");
        Assert.Equal(["name", "phones"], contact.EnumerateObject().Select(property => property.Name));
        Assert.Equal(name is null ? JsonValueKind.Null : JsonValueKind.String, contact.GetProperty("name").ValueKind);
        Assert.Equal(name, contact.GetProperty("name").GetString());
        Assert.Equal(phones, contact.GetProperty("phones").EnumerateArray().Select(phone => phone.GetString()));
    }
}
