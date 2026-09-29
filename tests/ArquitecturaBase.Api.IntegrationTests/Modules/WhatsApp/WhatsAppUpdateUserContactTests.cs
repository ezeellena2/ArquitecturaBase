using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp de la edición de un administrador (UpdateUserContactTests): la regla de los países
/// habilitados vale para un número nuevo, no para el que la cuenta ya tiene, y cambiar el número también suelta el
/// chat del anterior.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppUpdateUserContactTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Una cuenta puede tener un número de un país que no está habilitado: el bot crea cuentas con el número del chat, y
    /// achicar <c>WhatsApp:AllowedCountries</c> deja afuera números que ya estaban. La regla de los países es para un
    /// número nuevo: mandar de vuelta el que ya tiene no la rechaza, y el nombre y los roles se guardan.
    /// </summary>
    [Fact]
    public async Task Sending_back_a_phone_from_a_country_that_is_not_enabled_that_the_account_already_has_saves_the_rest()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var local = "99" + Random.Shared.Next(100_000, 1_000_000).ToString(CultureInfo.InvariantCulture);
        var uruguay = PhoneNumber.Create("+598" + local).Value;
        var user = await admin.CreateVerifiedAccountAsync(email: null, uruguay, "Juan Gómez");

        using var response = await admin.UpdateAsync(
            user.Id, new { displayName = "Juan Pérez", roles = Roles, phone = new { country = "UY", number = "0" + local } }, "es");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await admin.AccountAsync(user.Id);
        Assert.Equal("Juan Pérez", account.DisplayName);
        Assert.Equal(uruguay.Value, account.PhoneNumber);
        Assert.True(account.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Changing_the_phone_releases_the_chat_of_the_previous_one_and_voids_its_links_but_keeps_the_sessions()
    {
        using var person = factory.CreateClient();
        var email = TestEmails.Unique("cambianumero");
        var tokens = await person.LoginAsync(factory, email);
        using var me = await person.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var userId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();
        var (previousPhone, newPhone) = (TestPhones.Unique(), TestPhones.Unique());
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().SetPhoneAsync(userId, previousPhone, confirmed: true, Ct);
        });
        var bsuid = await BotConversation.WriteAsync(factory, person, previousPhone);
        var link = Assert.IsType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(previousPhone)[^1]);

        using var adminClient = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, adminClient);
        using var response = await admin.UpdateAsync(userId, new { roles = Roles, phone = AdminUsersApi.PhoneField(newPhone) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await admin.AccountAsync(userId);
        Assert.Equal(newPhone.Value, account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
        Assert.Null((await BotConversation.ContactAsync(factory, bsuid)).UserId);

        using var redeem = await person.PostJsonAsync("/account/login-link/redeem", new { token = BotConversation.TokenOf(link.Url) });
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());

        // Las sesiones siguen: cambiar un dato no es cortar el acceso. Para eso está desvincular.
        using var protectedCall = await person.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, protectedCall.StatusCode);
    }

    [Fact]
    public async Task A_phone_that_is_not_a_mobile_or_from_a_country_that_is_not_enabled_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var user = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("numeroraro"), phone: null);

        using var invalid = await admin.UpdateAsync(user.Id, new { roles = Roles, phone = new { country = "AR", number = "123" } });
        using var uruguay = await admin.UpdateAsync(user.Id, new { roles = Roles, phone = new { country = "UY", number = "099 123 456" } });

        Assert.Equal(UserErrors.PhoneInvalidCode, (await invalid.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(WhatsAppErrors.CountryNotSupportedCode, (await uruguay.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null((await admin.AccountAsync(user.Id)).PhoneNumber);
    }

    private static string[] Roles => [SystemRoles.User];
}
