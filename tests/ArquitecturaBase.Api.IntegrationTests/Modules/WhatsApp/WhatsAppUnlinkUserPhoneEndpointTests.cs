using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp de desvincular el número (UnlinkUserPhoneEndpointTests): también suelta el chat que
/// el bot vinculó a la cuenta, y el enlace que el bot ya mandó deja de servir.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppUnlinkUserPhoneEndpointTests(ApiFactory factory)
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
    public async Task Unlinking_an_account_without_a_number_answers_204()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var withoutPhone = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("sinnumero"), phone);
        var bsuid = await BotConversation.WriteAsync(factory, client, phone);
        var link = Assert.IsType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(phone)[^1]);
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().RemovePhoneAsync(withoutPhone.Id, Ct);
        });

        using var response = await admin.UnlinkPhoneAsync(withoutPhone.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await BotConversation.ContactAsync(factory, bsuid)).UserId);
        using var redeem = await client.PostJsonAsync(
            "/account/login-link/redeem", new { token = BotConversation.TokenOf(link.Url) });
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private async Task SetPhoneAsync(Guid userId, PhoneNumber phone) =>
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().SetPhoneAsync(userId, phone, confirmed: true, Ct);
        });
}
