using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Api.IntegrationTests.Settings;

[Collection(ApiTestGroup.Name)]
public sealed class SettingsControllerTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_reads_the_registration_mode()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        // Se cambia el modo después de ingresar: en InviteOnly, una cuenta que todavía no exista no recibiría código.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);

        using var response = await client.GetWithTokenAsync("/api/settings", tokens.AccessToken);
        var settings = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("InviteOnly", settings.GetProperty("registrationMode").GetString());
    }

    [Fact]
    public async Task Reading_the_settings_requires_the_settings_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("nosettings"));

        using var response = await client.GetWithTokenAsync("/api/settings", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Changing_the_mode_from_the_panel_takes_effect_without_restarting()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        // El scope deja el modo como estaba, aunque el test lo cambie con el endpoint.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var email = TestEmails.Unique("afterswitch");

        using var closed = await client.PostJsonAsync("/account/login-code", new { email });
        using var updated = await PutSettingsAsync(client, tokens.AccessToken, new { registrationMode = "Open" });
        var code = await client.RequestCodeAsync(factory, email);

        Assert.Equal(HttpStatusCode.Accepted, closed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);

        // Con el modo cerrado el pedido respondió 202 pero no llegó ningún email; con el cambio hecho desde el
        // panel, el código sale sin reiniciar nada. El único email de esta dirección es el de después del cambio.
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(1, factory.EmailSender.CountFor(email));
    }

    [Fact]
    public async Task An_unknown_registration_mode_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        // JsonStringEnumConverter acepta números: el que pone la lista blanca es el validador.
        using var response = await PutSettingsAsync(client, tokens.AccessToken, new { registrationMode = 7 }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "El modo de registro no es válido.",
            problem.GetProperty("errors").GetProperty("registrationMode")[0].GetString());
    }

    private static async Task<HttpResponseMessage> PutSettingsAsync(
        HttpClient client,
        string accessToken,
        object body,
        string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/api/settings", UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        return await client.SendAsync(request, Ct);
    }
}
