using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Lo que repiten los tests de la administración de usuarios (sección 12 del spec del ingreso con WhatsApp): el
/// administrador con su token real (o con la sesión de prueba, donde no se prueba el ingreso), el alta por el endpoint y
/// las cuentas armadas directo con Identity.
/// </summary>
internal sealed class AdminUsersApi(ApiFactory factory, HttpClient client, string? accessToken, Guid adminId)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient Client => client;

    /// <summary>El access token real; con la sesión de prueba no hay, y pedirlo hace fallar el test.</summary>
    public string AccessToken =>
        accessToken ?? throw new InvalidOperationException("The administrator uses the test session: it has no access token.");

    /// <summary>El administrador del seed: quien firma las invitaciones.</summary>
    public Guid AdminId => adminId;

    public static async Task<AdminUsersApi> SignInAsync(ApiFactory factory, HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);

        return new AdminUsersApi(factory, client, tokens.AccessToken, (await me.ReadJsonAsync()).GetProperty("id").GetGuid());
    }

    /// <summary>
    /// El administrador del seed con la sesión de prueba (X-Test-UserId), para los tests que no prueban el ingreso ni su
    /// token: se lee de la base y entra por código sólo si todavía no tiene cuenta. Cada código de más cuenta para el límite
    /// de pedidos de su correo (100 por ventana en el arnés), que comparten los tests que corren juntos.
    /// </summary>
    public static async Task<AdminUsersApi> WithTestSessionAsync(ApiFactory factory, HttpClient client) =>
        new(factory, client, accessToken: null, await RequestSessions.SeedAdminIdAsync(factory));

    /// <summary>Un pedido como este administrador: con su token o con la sesión de prueba.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? language = null) =>
        accessToken is null
            ? client.SendAsync(method, url, body is null ? null : JsonContent.Create(body), language,
                adminId.ToString("D", CultureInfo.InvariantCulture))
            : client.SendWithTokenAsync(method, url, accessToken, body, language);

    /// <summary>El número como lo carga el admin en el formulario: el país y el número tal como lo escribe.</summary>
    public static object PhoneField(PhoneNumber phone) => new { country = "AR", number = TestPhones.AsTypedLocally(phone) };

    public Task<HttpResponseMessage> CreateAsync(object body, string? language = null) =>
        SendAsync(HttpMethod.Post, "/api/users", body, language);

    /// <summary>El alta que tiene que salir bien: devuelve el id de la cuenta, nueva o restaurada.</summary>
    public async Task<Guid> CreateOkAsync(object body, string? language = null) =>
        (await CreatedAsync(body, language)).GetProperty("id").GetGuid();

    /// <summary>El alta que tiene que restaurar una cuenta borrada (N4): la respuesta lo dice. Devuelve su id.</summary>
    public async Task<Guid> RestoreOkAsync(object body, string? language = null)
    {
        var created = await CreatedAsync(body, language);
        Assert.True(created.GetProperty("restored").GetBoolean());

        return created.GetProperty("id").GetGuid();
    }

    /// <summary>El cuerpo de un alta que salió bien: el id y si se restauró una cuenta borrada.</summary>
    public async Task<JsonElement> CreatedAsync(object body, string? language = null)
    {
        using var response = await CreateAsync(body, language);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            Assert.Fail($"POST /api/users -> {response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return await response.ReadJsonAsync();
    }

    /// <summary>La edición, como <see cref="RevisionedEdits.PutWithRevisionAsync"/>: con la revisión que la cuenta tiene ahora.</summary>
    public async Task<HttpResponseMessage> UpdateAsync(Guid userId, object body, string? language = null)
    {
        if (accessToken is not null)
        {
            return await client.PutWithRevisionAsync($"/api/users/{userId}", accessToken, body, language);
        }

        var json = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
        if (!json.ContainsKey("expectedRevision"))
        {
            using var current = await SendAsync(HttpMethod.Get, $"/api/users/{userId}");
            json["expectedRevision"] = current.IsSuccessStatusCode
                ? (await current.ReadJsonAsync()).GetProperty("revision").GetInt64()
                : 1;
        }

        return await SendAsync(HttpMethod.Put, $"/api/users/{userId}", json, language);
    }

    public Task<HttpResponseMessage> InviteAsync(Guid userId, object body, string? language = null) =>
        SendAsync(HttpMethod.Post, $"/api/users/{userId}/invitation", body, language);

    public Task<HttpResponseMessage> UnlinkPhoneAsync(Guid userId, string? language = null) =>
        SendAsync(HttpMethod.Delete, $"/api/users/{userId}/whatsapp", body: null, language);

    public async Task<JsonElement> DetailAsync(Guid userId)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/api/users/{userId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.ReadJsonAsync();
    }

    public Task<UserAccount> AccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IUserReader>().FindByIdAsync(userId, Ct)));

    /// <summary>Una cuenta armada directo con Identity, con el correo y el número verificados.</summary>
    public Task<UserAccount> CreateVerifiedAccountAsync(string? email, PhoneNumber? phone, string? displayName = null) =>
        factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            email is null ? null : Email.Create(email).Value, phone, phoneConfirmed: true, displayName, "es", Ct));

    /// <summary>Borrado lógico, como el del endpoint: la cuenta conserva su correo y su número.</summary>
    public Task DeleteAccountAsync(Guid userId) =>
        factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().DeleteAsync(userId, Ct);
        });
}
