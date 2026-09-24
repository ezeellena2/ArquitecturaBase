using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// La edición de un administrador carga un correo o un número (sección 12 del spec del ingreso con WhatsApp). Ausente o
/// null no cambia nada: por acá no se borra un medio de ingreso. Lo que cambia queda sin verificar, con las mismas
/// reglas que el alta, y cambiar el número suelta el chat del anterior, como en el perfil, sin cerrar las sesiones.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UpdateUserContactTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Editing_adds_an_email_that_stays_unverified_and_keeps_the_rest()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var user = await admin.CreateVerifiedAccountAsync(email: null, phone, "Juan Gómez");
        var email = TestEmails.Unique("agregado");

        using var response = await admin.UpdateAsync(user.Id, new { displayName = "Juan Gómez", roles = Roles, email });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await admin.AccountAsync(user.Id);
        Assert.Equal(email, account.Email);
        Assert.False(account.EmailConfirmed);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.True(account.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Without_an_email_or_a_phone_nothing_changes()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("igual");
        var phone = TestPhones.Unique();
        var user = await admin.CreateVerifiedAccountAsync(email, phone);

        using var response = await admin.UpdateAsync(user.Id, new { displayName = "Otro nombre", roles = Roles, email = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await admin.AccountAsync(user.Id);
        Assert.Equal(email, account.Email);
        Assert.True(account.EmailConfirmed);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.True(account.PhoneNumberConfirmed);
        Assert.Equal("Otro nombre", account.DisplayName);
    }

    [Fact]
    public async Task Sending_the_same_email_and_phone_keeps_them_verified()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("mismo");
        var phone = TestPhones.Unique();
        var user = await admin.CreateVerifiedAccountAsync(email, phone);

        using var response = await admin.UpdateAsync(
            user.Id, new { roles = Roles, email = email.ToUpperInvariant(), phone = AdminUsersApi.PhoneField(phone) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await admin.AccountAsync(user.Id);
        Assert.True(account.EmailConfirmed);
        Assert.True(account.PhoneNumberConfirmed);
    }

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
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetPhoneAsync(userId, previousPhone, confirmed: true, Ct);

            return 0;
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
    public async Task An_email_or_a_phone_of_another_account_active_or_deleted_is_rejected()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var user = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("editada"), phone: null);
        var active = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("activa"), TestPhones.Unique());
        var deleted = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("borrada"), TestPhones.Unique());
        await admin.DeleteAccountAsync(deleted.Id);

        using var activeEmail = await admin.UpdateAsync(user.Id, new { roles = Roles, email = active.Email }, "es");
        using var deletedEmail = await admin.UpdateAsync(user.Id, new { roles = Roles, email = deleted.Email });
        using var activePhone = await admin.UpdateAsync(
            user.Id, new { roles = Roles, phone = AdminUsersApi.PhoneField(PhoneNumber.Create(active.PhoneNumber).Value) }, "es");
        using var deletedPhone = await admin.UpdateAsync(
            user.Id, new { roles = Roles, phone = AdminUsersApi.PhoneField(PhoneNumber.Create(deleted.PhoneNumber).Value) });

        Assert.Equal(HttpStatusCode.Conflict, activeEmail.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, (await activeEmail.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, deletedEmail.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, (await deletedEmail.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, activePhone.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, (await activePhone.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, deletedPhone.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, (await deletedPhone.ReadJsonAsync()).GetProperty("code").GetString());

        var account = await admin.AccountAsync(user.Id);
        Assert.Equal(user.Email, account.Email);
        Assert.Null(account.PhoneNumber);
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
