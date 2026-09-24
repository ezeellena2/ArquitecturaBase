using System.Net;
using System.Text.Json;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Lo que repiten los tests de la administración de usuarios (sección 12 del spec del ingreso con WhatsApp): el
/// administrador con su token real, el alta por el endpoint y las cuentas armadas directo con Identity.
/// </summary>
internal sealed class AdminUsersApi(ApiFactory factory, HttpClient client, string accessToken, Guid adminId)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public HttpClient Client => client;

    public string AccessToken => accessToken;

    /// <summary>El administrador del seed: quien firma las invitaciones.</summary>
    public Guid AdminId => adminId;

    public static async Task<AdminUsersApi> SignInAsync(ApiFactory factory, HttpClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);

        return new AdminUsersApi(factory, client, tokens.AccessToken, (await me.ReadJsonAsync()).GetProperty("id").GetGuid());
    }

    /// <summary>El número como lo carga el admin en el formulario: el país y el número tal como lo escribe.</summary>
    public static object PhoneField(PhoneNumber phone) => new { country = "AR", number = TestPhones.AsTypedLocally(phone) };

    public Task<HttpResponseMessage> CreateAsync(object body, string? language = null) =>
        client.SendWithTokenAsync(HttpMethod.Post, "/api/users", accessToken, body, language);

    /// <summary>El alta que tiene que salir bien: devuelve el id de la cuenta.</summary>
    public async Task<Guid> CreateOkAsync(object body, string? language = null)
    {
        using var response = await CreateAsync(body, language);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"POST /api/users -> {response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    public Task<HttpResponseMessage> UpdateAsync(Guid userId, object body, string? language = null) =>
        client.SendWithTokenAsync(HttpMethod.Put, $"/api/users/{userId}", accessToken, body, language);

    public Task<HttpResponseMessage> InviteAsync(Guid userId, object body, string? language = null) =>
        client.SendWithTokenAsync(HttpMethod.Post, $"/api/users/{userId}/invitation", accessToken, body, language);

    public Task<HttpResponseMessage> UnlinkPhoneAsync(Guid userId, string? language = null) =>
        client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{userId}/whatsapp", accessToken, body: null, language);

    public async Task<JsonElement> DetailAsync(Guid userId)
    {
        using var response = await client.GetWithTokenAsync($"/api/users/{userId}", accessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.ReadJsonAsync();
    }

    public Task<UserAccount> AccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IIdentityService>().FindByIdAsync(userId, Ct)));

    /// <summary>Una cuenta armada directo con Identity, con el correo y el número verificados.</summary>
    public Task<UserAccount> CreateVerifiedAccountAsync(string? email, PhoneNumber? phone, string? displayName = null) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            email is null ? null : Email.Create(email).Value, phone, phoneConfirmed: true, displayName, "es", Ct));

    /// <summary>Borrado lógico, como el del endpoint: la cuenta conserva su correo y su número.</summary>
    public Task DeleteAccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().DeleteAsync(userId, Ct);

            return 0;
        });
}
