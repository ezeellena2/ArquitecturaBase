using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// W1-A: a location that arrives keeps only its point, rounded to six decimals like the app's; the name and address of a chosen
/// place are not kept. They do not use the database.
/// </summary>
public sealed class WhatsAppLocationWebhookReaderTests
{
    private const string WaId = "5493413654813";
    private const string Bsuid = "AR.1102953142229032";
    private static readonly DateTime SentAtUtc = new(2026, 11, 11, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_location_keeps_its_point_rounded_to_six_decimals_without_the_place()
    {
        var message = Read(MetaWebhook.Location("wamid.location", WaId, Bsuid, SentAtUtc, JsonValue.Create(-34.60372249m), JsonValue.Create(-58.381591m),
            "Obra Norte", "Calle Falsa 123"));

        Assert.Equal(WhatsAppMessageKind.Location, message.Kind);
        Assert.Null(message.Body);
        Assert.Null(message.ReplyId);
        Assert.Equal(new WhatsAppWebhookLocation(-34.603722m, -58.381591m), message.Location);
        Assert.DoesNotContain("34", message.Location!.ToString(), StringComparison.Ordinal);
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
