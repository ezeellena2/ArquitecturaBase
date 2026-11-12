using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

// W3 (task 22): the operator's chat shows a shared contact as a message of its own, with the name and the phones of its first card as they
// came, as the front's closed guard reads it (`name` always there, null when it has none, and 0 to 10 phones). Everyone who reads the chat
// sees it: it is no value of a response. It goes with the body when the history is cleared, and then `contact` is absent, never null. The
// list keeps a generic preview, without the name or the phones.
public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    private static WhatsAppMessageRow ContactRow(string? name, string? phonesJson) => new(Guid.NewGuid(), WhatsAppMessageDirection.Inbound,
        WhatsAppMessageKind.Contact, null, Now, 1, null, null, null, WhatsAppMessagePurpose.Business) { ContactName = name, ContactPhonesJson = phonesJson };

    private static JsonElement Shown(WhatsAppMessageResponse message) =>
        JsonDocument.Parse(JsonSerializer.Serialize(message, JsonSerializerOptions.Web)).RootElement.Clone();

    [Theory]
    [InlineData("Ana Gómez", """["+54 9 341 365-4813", "+54 341 444-5566"]""", new[] { "+54 9 341 365-4813", "+54 341 444-5566" })]
    [InlineData(null, """["+54 9 341 365-4813"]""", new[] { "+54 9 341 365-4813" })]
    [InlineData("Ana Gómez", "[]", new string[0])]
    [InlineData("Ana Gómez", """["1", "2", "3", "4", "5", "6", "7", "8", "9", "10"]""", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" })]
    public async Task A_contact_shows_the_name_and_the_phones_of_its_card_as_the_front_reads_them(string? name, string phonesJson, string[] phones)
    {
        var host = new Host();
        host.Reader.Audio = ContactRow(name, phonesJson);
        host.Source.Allowed = false;

        var message = Assert.Single((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);

        Assert.Equal((WhatsAppMessageKind.Contact, (string?)null), (message.Kind, message.Body));
        Assert.Equal(name, message.Contact!.Name);
        Assert.Equal(phones, message.Contact.Phones);
        var contact = Shown(message).GetProperty("contact");
        Assert.Equal(["name", "phones"], contact.EnumerateObject().Select(property => property.Name));
        Assert.Equal(name is null ? JsonValueKind.Null : JsonValueKind.String, contact.GetProperty("name").ValueKind);
        Assert.Equal(phones, contact.GetProperty("phones").EnumerateArray().Select(phone => phone.GetString()));
        Assert.Null(message.Document);
        Assert.Null(message.Location);
        Assert.DoesNotContain("341", message.Contact.ToString(), StringComparison.Ordinal);
    }

    // Retention takes the name and the phones with the body: the message keeps its kind and no contact. A card that does not read, or that
    // the front's closed guard would refuse (more than ten phones, an empty one), is left out the same way, so one bad row does not stop
    // the thread.
    [Theory]
    [InlineData(null, null)]
    [InlineData("Ana Gómez", "no es JSON")]
    [InlineData("Ana Gómez", """{"phones":["+54 9 341 365-4813"]}""")]
    [InlineData("Ana Gómez", """["+54 9 341 365-4813", null]""")]
    [InlineData("Ana Gómez", """["+54 9 341 365-4813", ""]""")]
    [InlineData("Ana Gómez", """["1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11"]""")]
    public async Task A_cleared_or_unreadable_contact_reaches_the_chat_without_one(string? name, string? phonesJson)
    {
        var host = new Host();
        host.Reader.Audio = ContactRow(name, phonesJson);

        var result = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct);

        var message = Assert.Single(result.Value.Messages);
        Assert.Equal((WhatsAppMessageKind.Contact, (WhatsAppSharedContactResponse?)null), (message.Kind, message.Contact));
        Assert.False(Shown(message).TryGetProperty("contact", out _));
    }

    [Fact]
    public async Task Any_other_message_keeps_its_shape_without_a_contact()
    {
        var host = new Host();

        var message = Assert.Single((await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), Guid.NewGuid(), Ct)).Value.Messages);

        Assert.Null(message.Contact);
        Assert.False(Shown(message).TryGetProperty("contact", out _));
    }

    [Theory]
    [InlineData("es", "Contacto")]
    [InlineData("en", "Contact")]
    public async Task Contact_list_preview_is_a_generic_label_without_its_name_or_phones(string culture, string preview)
    {
        using var scope = new CultureScope(culture);
        var host = new Host();
        host.Reader.PreviewKind = WhatsAppMessageKind.Contact;

        var result = await host.Service.ListAsync(new(), Guid.NewGuid(), Ct);

        Assert.Equal(preview, Assert.Single(result.Value.Page.Items).Preview);
    }
}
