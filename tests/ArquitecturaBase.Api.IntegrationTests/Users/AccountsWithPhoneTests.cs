using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Una cuenta puede no tener correo: todo lo que mostraba el correo como identidad muestra el número
/// (sección 6.1 del spec del ingreso con WhatsApp).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class AccountsWithPhoneTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_returns_the_phone_of_an_account_without_email()
    {
        var phone = TestPhones.Unique();
        var user = await CreatePhoneOnlyAsync(phone, "Laura");
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/api/me", userId: IdOf(user));
        var me = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, me.GetProperty("email").ValueKind);
        Assert.False(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(phone.Value, me.GetProperty("phoneNumber").GetString());
        Assert.True(me.GetProperty("phoneNumberConfirmed").GetBoolean());
        Assert.False(me.GetProperty("hasGoogleLogin").GetBoolean());
        Assert.Equal("Laura", me.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Me_says_whether_the_account_signs_in_with_google()
    {
        // El ingreso real con Google: el nombre del proveedor que guarda Identity tiene que ser el que busca /api/me.
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("megoogle");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var external = await client.PostJsonAsync("/test/external-login", new { providerKey, email, name = "Ana", emailVerified = true });
        using var callback = await client.SendAsync(HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(ReturnUrl));
        var userId = await factory.ExecuteDbContextAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync(Ct));

        using var response = await client.SendAsync(
            HttpMethod.Get, "/api/me", userId: userId.ToString("D", CultureInfo.InvariantCulture));
        var me = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.True(me.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("phoneNumber").ValueKind);
        Assert.True(me.GetProperty("hasGoogleLogin").GetBoolean());
    }

    [Fact]
    public async Task The_list_shows_the_phone_when_there_is_no_email()
    {
        var phone = TestPhones.Unique();
        var user = await CreatePhoneOnlyAsync(phone, displayName: null);
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/users?search=" + TestPhones.LocalPart(phone), tokens.AccessToken);
        var row = RowOf(await response.ReadJsonAsync(), user.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("email").ValueKind);
        Assert.Equal(phone.Value, row.GetProperty("phoneNumber").GetString());
        Assert.True(row.GetProperty("phoneNumberConfirmed").GetBoolean());
    }

    [Fact]
    public async Task The_detail_brings_the_email_and_the_phone_with_whether_they_are_verified()
    {
        var phone = TestPhones.Unique();
        var email = Email.Create(TestEmails.Unique("detail")).Value;
        var user = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateAsync(email, phone, phoneConfirmed: false, "Con los dos", "es", Ct));
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users/{IdOf(user)}", tokens.AccessToken);
        var detail = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email.Value, detail.GetProperty("email").GetString());
        Assert.True(detail.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(phone.Value, detail.GetProperty("phoneNumber").GetString());
        Assert.False(detail.GetProperty("phoneNumberConfirmed").GetBoolean());
    }

    [Theory]
    [InlineData("{0}")]
    [InlineData("{1} {2}")]
    [InlineData("+54 9 351 {1}-{2}")]
    public async Task Search_finds_users_by_phone_with_or_without_separators(string format)
    {
        var phone = TestPhones.Unique();
        var local = TestPhones.LocalPart(phone);
        var user = await CreatePhoneOnlyAsync(phone, displayName: null);
        var search = string.Format(CultureInfo.InvariantCulture, format, local, local[..3], local[3..]);
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var list = await client.GetWithTokenAsync("/api/users?search=" + Uri.EscapeDataString(search), tokens.AccessToken);
        using var counts = await client.GetWithTokenAsync("/api/users/filter-counts?search=" + Uri.EscapeDataString(search), tokens.AccessToken);
        var page = await list.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(user.Id, Ids(page));
        // Los conteos arman la consulta con el mismo filtro: tienen que contar lo mismo que trae el listado.
        Assert.Equal(
            page.GetProperty("totalCount").GetInt32(),
            (await counts.ReadJsonAsync()).GetProperty("status").GetProperty("all").GetInt32());
    }

    [Fact]
    public async Task A_search_with_less_than_four_digits_does_not_match_by_phone()
    {
        // "+54 9" son tres dígitos que están en todos los celulares argentinos: si contaran, la búsqueda
        // traería a todos. Ningún correo ni nombre tiene ese texto, así que no tiene que traer a nadie.
        var user = await CreatePhoneOnlyAsync(TestPhones.Unique(), displayName: null);
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/users?search=" + Uri.EscapeDataString("+54 9"), tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(user.Id, Ids(page));
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_search_with_letters_does_not_match_by_phone()
    {
        // Con letras, el texto es un nombre o un correo: sus dígitos ("juan1234567@…") no tienen que traer a quien
        // tiene esos dígitos en el número.
        var phone = TestPhones.Unique();
        var user = await CreatePhoneOnlyAsync(phone, displayName: null);
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync(
            "/api/users?search=" + Uri.EscapeDataString("juan" + TestPhones.LocalPart(phone)), tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(user.Id, Ids(page));
    }

    private Task<UserAccount> CreatePhoneOnlyAsync(PhoneNumber phone, string? displayName) =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateAsync(email: null, phone, phoneConfirmed: true, displayName, "es", Ct));

    private static string IdOf(UserAccount user) => user.Id.ToString("D", CultureInfo.InvariantCulture);

    private static Guid[] Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();

    private static JsonElement RowOf(JsonElement page, Guid userId) =>
        page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetGuid() == userId);
}
