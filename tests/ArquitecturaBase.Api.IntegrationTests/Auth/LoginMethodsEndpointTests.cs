using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// Qué medios de ingreso ofrece la pantalla de login (sección 10 del spec del ingreso con WhatsApp): sin su
/// configuración, Google y WhatsApp no aparecen. Con WhatsApp apagado lo prueba <see cref="WhatsAppLoginCodeTests"/>.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class LoginMethodsEndpointTests(ApiFactory factory)
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
            ["google", "whatsapp", "whatsappCountries", "whatsappNumber"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));

        // El arnés prende los dos: el ClientId de Google sale de appsettings.json y WhatsApp tiene su PhoneNumberId.
        Assert.True(body.GetProperty("google").GetBoolean());
        Assert.True(body.GetProperty("whatsapp").GetBoolean());
        Assert.Equal(["AR"], body.GetProperty("whatsappCountries").EnumerateArray().Select(country => country.GetString()));

        // Sin WhatsApp:DisplayPhoneNumber no hay número para el enlace "Volver a WhatsApp".
        Assert.Equal(JsonValueKind.Null, body.GetProperty("whatsappNumber").ValueKind);
    }

    [Fact]
    public async Task The_number_of_the_bot_comes_from_the_configuration()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("WhatsApp:DisplayPhoneNumber", "15551632662")
            .UseSetting("WhatsApp:AllowedCountries:1", "UY"));
        using var client = api.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, Url);
        var body = await response.ReadJsonAsync();

        Assert.Equal("15551632662", body.GetProperty("whatsappNumber").GetString());
        Assert.Equal(["AR", "UY"], body.GetProperty("whatsappCountries").EnumerateArray().Select(country => country.GetString()));
    }

    [Fact]
    public async Task Without_a_google_client_id_google_is_not_offered()
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
