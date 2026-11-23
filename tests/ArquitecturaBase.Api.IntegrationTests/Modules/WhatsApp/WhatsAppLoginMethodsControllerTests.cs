using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp de los medios de ingreso (LoginMethodsControllerTests): con WhatsApp prendido, la
/// pantalla de login lo ofrece con sus países y el número del bot, aunque Google esté apagado. Con WhatsApp apagado
/// lo prueba <c>WhatsAppLoginCodeTests</c>.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppLoginMethodsControllerTests(ApiFactory factory)
{
    private const string Url = "/account/login-methods";

    [Fact]
    public async Task Anyone_can_ask_which_methods_are_on_and_the_answer_has_the_contract_names()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, Url);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["google", "registrationOpen", "whatsapp", "whatsappCountries", "whatsappNumber"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        // El arnés prende los dos: Google tiene su ClientId y WhatsApp, su PhoneNumberId.
        Assert.True(body.GetProperty("google").GetBoolean());
        Assert.True(body.GetProperty("whatsapp").GetBoolean());
        Assert.Equal(["AR"], body.GetProperty("whatsappCountries").EnumerateArray().Select(country => country.GetString()));

        // Sin WhatsApp:DisplayPhoneNumber no hay número para el enlace "Volver a WhatsApp".
        Assert.Equal(JsonValueKind.Null, body.GetProperty("whatsappNumber").ValueKind);
    }

    [Fact]
    public async Task The_number_of_the_bot_comes_from_the_configuration_and_the_countries_from_the_settings()
    {
        // RC3-A: the countries are Configuración › WhatsApp; the number of the bot stays in appsettings.
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("WhatsApp:DisplayPhoneNumber", "15551632662")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<IWhatsAppSettingsReader>();
                services.AddScoped<IWhatsAppSettingsReader>(_ => new FixedWhatsAppSettingsReader(allowedCountries: ["AR", "UY"]));
            }));
        using var client = api.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, Url);
        var body = await response.ReadJsonAsync();

        Assert.Equal("15551632662", body.GetProperty("whatsappNumber").GetString());
        Assert.Equal(["AR", "UY"], body.GetProperty("whatsappCountries").EnumerateArray().Select(country => country.GetString()));
    }

    [Fact]
    public async Task Without_a_google_client_id_whatsapp_is_still_offered()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("Authentication:Google:ClientId", ""));
        using var client = api.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, Url);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(body.GetProperty("google").GetBoolean());
        Assert.True(body.GetProperty("whatsapp").GetBoolean());
    }
}
