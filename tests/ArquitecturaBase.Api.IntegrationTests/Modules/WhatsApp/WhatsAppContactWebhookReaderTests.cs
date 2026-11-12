using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// W3 (task 22): a contact the person shares comes as <c>Contact</c>, with the formatted name and the phones of its first card only, as
/// Meta sends them; the rest of the card (addresses, emails, company, links) is not read. A card without a phone or without a name is a
/// real case. The bodies have the shape of Meta's <c>contacts</c> message. They do not use the database.
/// </summary>
public sealed class WhatsAppContactWebhookReaderTests
{
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";
    private static readonly DateTime SentAtUtc = new(2026, 11, 12, 15, 0, 0, DateTimeKind.Utc);

    private const string AnaWithTwoPhones = """
        {"addresses":[{"city":"Rosario","country":"Argentina","country_code":"AR","state":"Santa Fe","street":"Bv. Oroño 1234","type":"WORK","zip":"S2000"}],
         "birthday":"1990-05-04","emails":[{"email":"ana@example.com","type":"WORK"}],
         "name":{"formatted_name":"Ana Gómez","first_name":"Ana","last_name":"Gómez"},
         "org":{"company":"Distribuidora de prueba","department":"Ventas","title":"Encargada"},
         "phones":[{"phone":"+54 9 341 365-4813","wa_id":"5493413654813","type":"CELL"},{"phone":"+54 341 444-5566","type":"WORK"}],
         "urls":[{"url":"https://example.com","type":"WORK"}]}
        """;

    [Fact]
    public void A_contact_keeps_the_formatted_name_and_both_phones_of_its_card_as_they_came()
    {
        var message = Read(AnaWithTwoPhones);

        Assert.Equal((WhatsAppMessageKind.Contact, (string?)null, (string?)null), (message.Kind, message.Body, message.ReplyId));
        Assert.Equal("Ana Gómez", message.SharedContact!.Name);
        Assert.Equal(["+54 9 341 365-4813", "+54 341 444-5566"], message.SharedContact.Phones);
        Assert.DoesNotContain("Ana", message.SharedContact.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("341", message.SharedContact.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_contact_without_a_phone_keeps_its_name_and_no_phones()
    {
        var message = Read("""{"emails":[{"email":"ana@example.com","type":"HOME"}],"name":{"formatted_name":"Ana Gómez","first_name":"Ana"}}""");

        Assert.Equal(WhatsAppMessageKind.Contact, message.Kind);
        Assert.Equal("Ana Gómez", message.SharedContact!.Name);
        Assert.Empty(message.SharedContact.Phones);
    }

    [Fact]
    public void A_contact_without_a_name_keeps_its_phones()
    {
        var message = Read("""{"phones":[{"phone":"+54 9 341 365-4813","wa_id":"5493413654813","type":"CELL"}]}""");

        Assert.Null(message.SharedContact!.Name);
        Assert.Equal(["+54 9 341 365-4813"], message.SharedContact.Phones);
    }

    [Fact]
    public void Of_two_cards_only_the_first_is_kept()
    {
        var message = Read(AnaWithTwoPhones, """{"name":{"formatted_name":"Bruno Díaz"},"phones":[{"phone":"+54 351 555-0000","type":"HOME"}]}""");

        Assert.Equal("Ana Gómez", message.SharedContact!.Name);
        Assert.Equal(["+54 9 341 365-4813", "+54 341 444-5566"], message.SharedContact.Phones);
    }

    // What is not a card does not stop the webhook: the message stays a contact without one, like a location without a point.
    [Theory]
    [InlineData("""[]""")]
    [InlineData("""["Ana Gómez"]""")]
    [InlineData(null)]
    public void A_contacts_message_without_a_card_stays_a_contact_without_one(string? cards)
    {
        var inbound = Inbound();
        if (cards is not null) inbound["contacts"] = JsonNode.Parse(cards);

        var message = Read(inbound);

        Assert.Equal(WhatsAppMessageKind.Contact, message.Kind);
        Assert.Null(message.SharedContact);
    }

    private static WhatsAppWebhookMessage Read(params string[] cards)
    {
        var inbound = Inbound();
        inbound["contacts"] = new JsonArray([.. cards.Select(card => JsonNode.Parse(card))]);
        return Read(inbound);
    }

    private static JsonObject Inbound() => new()
    {
        ["from"] = WaId, ["from_user_id"] = Bsuid, ["id"] = "wamid.contact", ["timestamp"] = MetaWebhook.Timestamp(SentAtUtc), ["type"] = "contacts",
    };

    private static WhatsAppWebhookMessage Read(JsonObject inbound)
    {
        var reader = new WhatsAppWebhookReader(Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "fictional-key" }),
            NullLogger<WhatsAppWebhookReader>.Instance);
        return Assert.Single(reader.Read(MetaWebhook.Build(contacts: [MetaWebhook.Contact(WaId, Bsuid, "Persona de prueba")], messages: [inbound])).Messages);
    }
}
