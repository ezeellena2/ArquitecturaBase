using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppCatalogShownPayloadTests
{
    [Fact]
    public void The_flow_sends_only_the_frozen_title_and_description_chosen_for_whatsapp()
    {
        var option = new StructuredFormCatalogOption(Guid.NewGuid(), "Private item text", "Private item detail",
            Value: "123456789012345678", Shown: new("Chosen title", "Chosen description"));
        var item = Offered(option);
        Assert.Equal(new[] { "id", "title", "description" }, item.Select(property => property.Key));
        Assert.Equal(option.Id.ToString("D"), item["id"]!.GetValue<string>());
        Assert.Equal("Chosen title", item["title"]!.GetValue<string>());
        Assert.Equal("Chosen description", item["description"]!.GetValue<string>());
        var bytes = item.ToJsonString();
        Assert.DoesNotContain("Private item", bytes, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789012345678", bytes, StringComparison.Ordinal);
        Assert.DoesNotContain("Integer", bytes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_chosen_presentation_without_description_never_falls_back_to_the_private_item_detail(string? description)
    {
        var option = new StructuredFormCatalogOption(Guid.NewGuid(), "Private original", "Private detail",
            Shown: new("Title only", description));
        var item = Offered(option);
        Assert.Equal("Title only", item["title"]!.GetValue<string>());
        Assert.False(item.ContainsKey("description"));
        Assert.Equal(2, item.Count);
    }

    [Fact]
    public void A_legacy_offer_without_shown_preserves_the_original_title_and_optional_detail()
    {
        var withDetail = Offered(new(Guid.NewGuid(), "Legacy text", "Legacy detail"));
        Assert.Equal("Legacy text", withDetail["title"]!.GetValue<string>());
        Assert.Equal("Legacy detail", withDetail["description"]!.GetValue<string>());
        var withoutDetail = Offered(new(Guid.NewGuid(), "Legacy text", null));
        Assert.False(withoutDetail.ContainsKey("description"));
        var legacy = JsonSerializer.Serialize(new StructuredFormCatalogOption(Guid.NewGuid(), "Legacy", null), JsonSerializerOptions.Web);
        Assert.DoesNotContain("shown", legacy, StringComparison.Ordinal);
    }

    private static JsonObject Offered(StructuredFormCatalogOption option)
    {
        var settings = new WhatsAppOptions { WabaId = "1", PhoneNumberId = "2", GraphApiVersion = "v25.0" };
        var field = Guid.NewGuid(); var key = "field_" + field.ToString("N");
        var message = new WhatsAppFlowMessageResponse(PhoneNumber.Create("+12025550123").Value, "Open form", "Open", "123", "test-token", "FORM",
            new("1", "2", "v25.0", "configuration"), [new(field, key, "Question", Guid.NewGuid(), 1, "Manual", "Catalog", [option])]);
        var navigation = WhatsAppMessagePayload.Build(message, settings)["interactive"]!["action"]!["parameters"]!["flow_action_payload"]!;
        return Assert.IsType<JsonObject>(Assert.Single(navigation["data"]![key + "_options"]!.AsArray()));
    }
}
