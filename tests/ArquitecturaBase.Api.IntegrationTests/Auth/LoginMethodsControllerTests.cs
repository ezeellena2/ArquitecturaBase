using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Application.Interfaces.Channels;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// Qué medios de ingreso ofrece la pantalla de login (sección 10 del spec del ingreso con WhatsApp): sin su
/// configuración, Google no aparece, y sin un canal de teléfono prendido tampoco WhatsApp. Con WhatsApp prendido lo
/// prueba <c>WhatsAppLoginMethodsControllerTests</c>, del módulo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class LoginMethodsControllerTests(ApiFactory factory)
{
    private const string Url = "/account/login-methods";

    /// <summary>
    /// Los nombres del contrato y la respuesta sin canal de teléfono, la de una copia sin módulos: Google según su
    /// ClientId, que sale de appsettings.json, y WhatsApp apagado, sin países ni número. El test cambia el canal por el
    /// apagado para afirmar lo mismo con y sin el módulo.
    /// </summary>
    [Fact]
    public async Task Anyone_can_ask_which_methods_are_on_and_the_answer_has_the_contract_names()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IPhoneChannel, DisabledPhoneChannel>())));
        using var client = api.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, Url);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["google", "registrationOpen", "whatsapp", "whatsappCountries", "whatsappNumber"],
            body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.True(body.GetProperty("google").GetBoolean());
        Assert.False(body.GetProperty("whatsapp").GetBoolean());
        Assert.Empty(body.GetProperty("whatsappCountries").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("whatsappNumber").ValueKind);
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
    }
}
