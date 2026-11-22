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
        using var updated = await PatchSettingsAsync(client, tokens.AccessToken, new { registrationMode = "Open" });
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
        using var response = await PatchSettingsAsync(client, tokens.AccessToken, new { registrationMode = 7 }, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "El modo de registro no es válido.",
            problem.GetProperty("errors").GetProperty("registrationMode")[0].GetString());
    }

    // RC6-B and RC1-A: the name, the sender, the logo and the sign-in periods are saved together with one revision and read back.
    [Fact]
    public async Task Admin_saves_and_reads_the_branding_and_the_retention_periods()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        await using var restore = await SystemSettingsScope.ChangeAsync(factory);

        using var updated = await PatchSettingsAsync(client, tokens.AccessToken, new
        {
            branding = new { appName = " Campo ", emailSenderName = "Campo Avisos", emailLogoUrl = "https://cdn.example.com/campo.png" },
            retention = new { loginAuditMonths = 6, loginCodeDays = 2, invitationDays = 10 },
        });
        using var response = await client.GetWithTokenAsync("/api/settings", tokens.AccessToken);
        var settings = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        var branding = settings.GetProperty("branding");
        Assert.Equal(("Campo", "Campo Avisos", "https://cdn.example.com/campo.png"), (branding.GetProperty("appName").GetString(),
            branding.GetProperty("emailSenderName").GetString(), branding.GetProperty("emailLogoUrl").GetString()));
        var retention = settings.GetProperty("retention");
        Assert.Equal((6, 2, 10), (retention.GetProperty("loginAuditMonths").GetInt32(), retention.GetProperty("loginCodeDays").GetInt32(),
            retention.GetProperty("invitationDays").GetInt32()));
    }

    // RC6-B: the code that goes out after the change already carries the new name, without restarting anything.
    [Fact]
    public async Task The_code_email_carries_the_name_saved_in_appearance_without_restarting()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        await using var restore = await SystemSettingsScope.ChangeAsync(factory);
        var email = TestEmails.Unique("branding");

        using var updated = await PatchSettingsAsync(client, tokens.AccessToken, new
        {
            branding = new { appName = "Campo", emailSenderName = "Campo Avisos", emailLogoUrl = (string?)null },
        });
        var code = await client.RequestCodeAsync(factory, email);
        var message = await factory.EmailSender.WaitForAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Equal($"{code} es tu código de acceso a Campo", message.Subject);
    }

    [Fact]
    public async Task Each_branding_and_retention_field_reports_its_own_problem()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        await using var restore = await SystemSettingsScope.ChangeAsync(factory);

        using var response = await PatchSettingsAsync(client, tokens.AccessToken, new
        {
            branding = new { appName = " ", emailSenderName = "Campo", emailLogoUrl = "http://cdn.example.com/campo.png" },
            retention = new { loginAuditMonths = 12, loginCodeDays = 30, invitationDays = 6 },
        }, "es");
        var errors = (await response.ReadJsonAsync()).GetProperty("errors");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Este campo es obligatorio.", errors.GetProperty("branding.appName")[0].GetString());
        Assert.Equal("Escribí una dirección que empiece con https://.", errors.GetProperty("branding.emailLogoUrl")[0].GetString());
        Assert.Equal("Elegí un número entre 7 y 3.650.", errors.GetProperty("retention.invitationDays")[0].GetString());
    }

    private static async Task<HttpResponseMessage> PatchSettingsAsync(
        HttpClient client,
        string accessToken,
        object body,
        string? language = null)
    {
        // La sección, con la revisión que tiene ahora la configuración, como la pantalla (el PUT se retiró, N2).
        var json = System.Text.Json.JsonSerializer.SerializeToNode(body, System.Text.Json.JsonSerializerOptions.Web)!.AsObject();
        using (var current = await client.GetWithTokenAsync("/api/settings", accessToken))
        {
            json["expectedRevision"] = (await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
        }

        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri("/api/settings", UriKind.Relative))
        {
            Content = JsonContent.Create(json),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        return await client.SendAsync(request, Ct);
    }
}
