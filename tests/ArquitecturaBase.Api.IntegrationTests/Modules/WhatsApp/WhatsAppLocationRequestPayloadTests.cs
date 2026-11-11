using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// W1-A: the location request goes as Meta's <c>location_request_message</c>, with its text and the <c>send_location</c> action;
/// WhatsApp draws «Enviar ubicación» in the phone's language. It does not use the database.
/// </summary>
public sealed class WhatsAppLocationRequestPayloadTests
{
    [Fact]
    public void The_location_request_goes_as_an_interactive_location_request_with_its_text()
    {
        var payload = WhatsAppMessagePayload.Build(new WhatsAppLocationRequestMessage(PhoneNumber.Create("+5493413654800").Value,
            "Para completar «Registro de visita», mandanos tu ubicación."), new WhatsAppOptions { WabaId = "1", PhoneNumberId = "2", GraphApiVersion = "v25.0" });

        Assert.Equal("interactive", payload["type"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""
            {"type":"location_request_message","body":{"text":"Para completar «Registro de visita», mandanos tu ubicación."},"action":{"name":"send_location"}}
            """), payload["interactive"]));
    }
}
