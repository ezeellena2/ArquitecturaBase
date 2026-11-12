using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// W1-A: a location that arrives keeps its point, rounded to six decimals like the app's; W3 keeps the address of a chosen place when
/// Meta sends one, never its name. They do not use the database.
/// </summary>
public sealed class WhatsAppLocationWebhookReaderTests
{
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";
    private static readonly DateTime SentAtUtc = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_location_keeps_its_point_rounded_to_six_decimals_and_the_address_without_the_name_of_the_place()
    {
        var message = Read(MetaWebhook.Location("wamid.location", WaId, Bsuid, SentAtUtc, JsonValue.Create(-34.60372249m), JsonValue.Create(-58.381591m),
            "Obra Norte", "Calle Falsa 123"));

        Assert.Equal(WhatsAppMessageKind.Location, message.Kind);
        Assert.Null(message.Body);
        Assert.Null(message.ReplyId);
        Assert.Equal(new WhatsAppWebhookLocation(-34.603722m, -58.381591m, "Calle Falsa 123"), message.Location);
        Assert.DoesNotContain("34", message.Location!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Calle", message.Location.ToString(), StringComparison.Ordinal);
    }

    // W3: the bodies as Meta sends them: a place chosen on the map brings its name, its address and a link; one without an address
    // only its name; the current location, only the point.
    [Theory]
    [InlineData("""{"address":"Bv. Oroño 1234, S2000 Rosario, Santa Fe","latitude":-32.947102,"longitude":-60.638955,"name":"Hospital de prueba","url":"https://maps.google.com/?q=-32.947102,-60.638955"}""",
        "Bv. Oroño 1234, S2000 Rosario, Santa Fe")]
    [InlineData("""{"latitude":-32.947102,"longitude":-60.638955,"name":"Plaza de prueba"}""", null)]
    [InlineData("""{"latitude":-32.947102,"longitude":-60.638955}""", null)]
    public void A_place_keeps_the_address_Meta_sends_and_the_current_location_only_its_point(string location, string? address)
    {
        var inbound = new JsonObject
        {
            ["from"] = WaId, ["from_user_id"] = Bsuid, ["id"] = "wamid.place", ["timestamp"] = MetaWebhook.Timestamp(SentAtUtc),
            ["location"] = JsonNode.Parse(location), ["type"] = "location",
        };

        var message = Read(inbound);

        Assert.Equal((WhatsAppMessageKind.Location, (string?)null), (message.Kind, message.Body));
        Assert.Equal(new WhatsAppWebhookLocation(-32.947102m, -60.638955m, address), message.Location);
    }

    [Theory]
    [InlineData("\"-34.6\"", "-58.38")]
    [InlineData("-90.5", "-58.38")]
    [InlineData("-34.6", "180.5")]
    public void A_location_without_a_valid_point_stays_a_location_without_one(string latitude, string longitude)
    {
        var message = Read(MetaWebhook.Location("wamid.bad", WaId, Bsuid, SentAtUtc, JsonNode.Parse(latitude)!, JsonNode.Parse(longitude)!));

        Assert.Equal(WhatsAppMessageKind.Location, message.Kind);
        Assert.Null(message.Location);
    }

    private static WhatsAppWebhookMessage Read(JsonObject inbound)
    {
        var reader = new WhatsAppWebhookReader(Options.Create(new WhatsAppOptions { PhoneNumberId = ApiFactory.WhatsAppPhoneNumberId, AccessToken = "fictional-key" }),
            NullLogger<WhatsAppWebhookReader>.Instance);
        return Assert.Single(reader.Read(MetaWebhook.Build(contacts: [MetaWebhook.Contact(WaId, Bsuid, "Ana")], messages: [inbound])).Messages);
    }
}
