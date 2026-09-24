using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// El admin desvincula el WhatsApp de alguien (sección 12 del spec del ingreso con WhatsApp): el caso del teléfono
/// robado. Como desactivar, corta el acceso en el momento: le saca el número, suelta su chat e invalida los enlaces que
/// el bot ya mandó, y cierra las sesiones abiertas. Puede dejar a alguien sin medio de ingreso, salvo a sí mismo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class UnlinkUserPhoneEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unlinking_removes_the_number_releases_the_chat_voids_the_links_and_closes_the_sessions()
    {
        // La persona entra de verdad con su correo: le queda el access token y la cookie.
        using var person = factory.CreateClient();
        var email = TestEmails.Unique("robado");
        var tokens = await person.LoginAsync(factory, email);
        using var me = await person.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var userId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();
        var phone = TestPhones.Unique();
        await SetPhoneAsync(userId, phone);

        // El bot le manda un enlace al chat y vincula el contacto a la cuenta.
        var bsuid = await BotConversation.WriteAsync(factory, person, phone);
        var link = Assert.IsType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(phone)[^1]);
        Assert.Equal(userId, (await BotConversation.ContactAsync(factory, bsuid)).UserId);

        using var adminClient = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, adminClient);
        using var response = await admin.UnlinkPhoneAsync(userId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var account = await admin.AccountAsync(userId);
        Assert.Null(account.PhoneNumber);
        Assert.False(account.PhoneNumberConfirmed);
        Assert.Null((await BotConversation.ContactAsync(factory, bsuid)).UserId);

        // El access token que ya tenía deja de valer, sin esperar los 15 minutos.
        using var after = await person.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // Y el enlace que ya estaba en el chat tampoco sirve.
        using var redeem = await person.PostJsonAsync(
            "/account/login-link/redeem", new { token = BotConversation.TokenOf(link.Url) });
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_administrator_can_leave_someone_without_a_way_to_sign_in()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var only = await admin.CreateVerifiedAccountAsync(email: null, TestPhones.Unique());

        using var response = await admin.UnlinkPhoneAsync(only.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await admin.AccountAsync(only.Id)).PhoneNumber);
    }

    [Fact]
    public async Task An_administrator_does_not_leave_themselves_without_a_way_to_sign_in()
    {
        // Un administrador que entra solo con WhatsApp: sin su número no le quedaría cómo entrar.
        var phone = TestPhones.Unique();
        var self = await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            var account = await identity.CreateAsync(email: null, phone, phoneConfirmed: true, "Solo WhatsApp", "es", Ct);
            await identity.SetRolesAsync(account.Id, [SystemRoles.Admin], Ct);

            return account;
        });
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Delete, $"/api/users/{self.Id}/whatsapp", language: "es", userId: IdOf(self.Id));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.LastLoginMethodCode, problem.GetProperty("code").GetString());
        Assert.Equal(phone.Value, (await AccountAsync(self.Id)).PhoneNumber);
    }

    [Fact]
    public async Task An_administrator_with_a_verified_email_can_unlink_their_own_number()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        await SetPhoneAsync(admin.AdminId, phone);

        using var response = await admin.UnlinkPhoneAsync(admin.AdminId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await admin.AccountAsync(admin.AdminId)).PhoneNumber);

        // Es la acción del teléfono robado, también sobre la propia cuenta: cierra todas las sesiones, la suya incluida,
        // y vuelve a entrar con el correo. Sin cerrar sesiones, se desvincula desde Mi perfil.
        using var after = await client.GetWithTokenAsync("/test/protected", admin.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Unlinking_an_account_without_a_number_answers_204()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var withoutPhone = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("sinnumero"), phone: null);

        using var response = await admin.UnlinkPhoneAsync(withoutPhone.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task An_account_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        using var response = await admin.UnlinkPhoneAsync(Guid.CreateVersion7());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private static string IdOf(Guid userId) => userId.ToString("D", CultureInfo.InvariantCulture);

    private async Task SetPhoneAsync(Guid userId, PhoneNumber phone) =>
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetPhoneAsync(userId, phone, confirmed: true, Ct);

            return 0;
        });

    private Task<UserAccount> AccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IIdentityService>().FindByIdAsync(userId, Ct)));
}
