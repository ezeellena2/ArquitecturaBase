using System.Text.Json;
using ArquitecturaBase.Api.Endpoints.Connect;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// Los tokens de una cuenta sin correo: el claim email va solo si hay correo, y name pasa a ser
/// DisplayName ?? Email ?? número (sección 6.1 del spec del ingreso con WhatsApp).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class PhoneAccountTokensTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_token_of_an_account_without_email_has_no_email_and_is_named_by_the_phone()
    {
        // Todavía no hay un ingreso con el número: se arma la identidad con la misma fábrica que usa /connect/authorize.
        var phone = TestPhones.Unique();
        var user = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateAsync(email: null, phone, phoneConfirmed: true, displayName: null, "es", Ct));

        var principal = await factory.ExecuteScopeAsync(services => services.GetRequiredService<OpenIdPrincipalFactory>()
            .CreateAsync(user.Id, [Scopes.OpenId, Scopes.Email, Scopes.Profile, Scopes.Roles], Ct));

        Assert.NotNull(principal);
        Assert.Null(principal.GetClaim(Claims.Email));
        Assert.Equal(phone.Value, principal.GetClaim(Claims.Name));
        Assert.Equal(["User"], principal.GetClaims(Claims.Role));
    }

    [Fact]
    public async Task The_display_name_still_wins_over_the_phone()
    {
        var user = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>()
            .CreateAsync(email: null, TestPhones.Unique(), phoneConfirmed: true, "Laura Ríos", "es", Ct));

        var principal = await factory.ExecuteScopeAsync(services => services.GetRequiredService<OpenIdPrincipalFactory>()
            .CreateAsync(user.Id, [Scopes.OpenId, Scopes.Profile], Ct));

        Assert.Equal("Laura Ríos", principal!.GetClaim(Claims.Name));
    }

    [Fact]
    public async Task Refreshing_drops_the_email_that_the_account_no_longer_has()
    {
        // El refresh parte de los claims guardados en el refresh token: si no se borrara, el email del ingreso
        // seguiría viajando en cada token nuevo aunque la cuenta ya no lo tenga.
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("sincorreo");
        var tokens = await client.LoginAsync(factory, email);
        var phone = TestPhones.Unique();

        // Todavía no hay un caso de uso que saque el correo: se escribe directo en la base.
        await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(candidate => candidate.Email == email, Ct);
            user.Email = null;
            user.NormalizedEmail = null;
            user.EmailConfirmed = false;
            user.PhoneNumber = phone.Value;
            user.PhoneNumberConfirmed = true;

            return await db.SaveChangesAsync(Ct);
        });

        using var refresh = await client.RefreshAsync(tokens.RefreshToken);
        var refreshed = await TokenResponse.ReadAsync(refresh);
        using var userInfo = await client.GetWithTokenAsync("/connect/userinfo", refreshed.AccessToken);
        var info = await userInfo.ReadJsonAsync();

        Assert.Equal(email, PayloadOf(tokens.IdToken).GetProperty(Claims.Email).GetString());
        var idToken = PayloadOf(refreshed.IdToken);
        Assert.False(idToken.TryGetProperty(Claims.Email, out _));
        Assert.Equal(phone.Value, idToken.GetProperty(Claims.Name).GetString());
        Assert.False(info.TryGetProperty(Claims.Email, out _));
        Assert.False(info.TryGetProperty(Claims.EmailVerified, out _));
        Assert.Equal(phone.Value, info.GetProperty(Claims.Name).GetString());
    }

    /// <summary>El id token va firmado pero no cifrado: el payload se lee sin claves.</summary>
    private static JsonElement PayloadOf(string? idToken)
    {
        Assert.NotNull(idToken);

        using var document = JsonDocument.Parse(WebEncoders.Base64UrlDecode(idToken.Split('.')[1]));

        return document.RootElement.Clone();
    }
}
