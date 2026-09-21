using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class DeactivateUserEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deactivating_cuts_off_the_session_that_was_already_open()
    {
        // La víctima entra de verdad: le queda el access token, el refresh token y la cookie.
        using var victim = factory.CreateClient();
        var email = TestEmails.Unique("corte");
        var tokens = await victim.LoginAsync(factory, email);
        var userId = await IdOfAsync(email);

        using var before = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);

        using var admin = factory.CreateClient();
        var adminTokens = await admin.LoginAsync(factory, ApiFactory.AdminEmail);
        using var deactivate = await admin.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{userId}/deactivate", adminTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        // El access token que ya tenía deja de valer, sin esperar los 15 minutos.
        using var after = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);

        // El refresh token tampoco sirve para conseguir uno nuevo.
        using var refresh = await victim.RefreshAsync(tokens.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);

        // Y la cookie ya no alcanza para pedir otro authorization code: vuelve al login.
        using var authorize = await victim.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith("/login?returnUrl=", authorize.Headers.Location!.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deactivating_renews_the_security_stamp()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("stamp");
        var userId = await CreateAsync(client, tokens.AccessToken, email);
        var before = await SecurityStampOfAsync(userId);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{userId}/deactivate", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.NotEqual(before, await SecurityStampOfAsync(userId));
    }

    [Fact]
    public async Task Activating_puts_the_account_back()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("reactivar"));
        using var off = await client.SendWithTokenAsync(HttpMethod.Post, $"/api/users/{userId}/deactivate", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, off.StatusCode);

        using var on = await client.SendWithTokenAsync(HttpMethod.Post, $"/api/users/{userId}/activate", tokens.AccessToken);
        using var read = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, on.StatusCode);
        Assert.True((await read.ReadJsonAsync()).GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Nobody_deactivates_their_own_account()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var myId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{myId}/deactivate", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.CannotModifySelfCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{Guid.CreateVersion7()}/deactivate", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deactivating_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinbaja"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{Guid.CreateVersion7()}/deactivate", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static async Task<Guid> CreateAsync(HttpClient client, string accessToken, string email)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", accessToken, new { email, roles = new[] { SystemRoles.User } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    private Task<Guid> IdOfAsync(string email) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Email == email).Select(user => user.Id).SingleAsync(Ct));

    private Task<string?> SecurityStampOfAsync(Guid userId) =>
        factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Id == userId).Select(user => user.SecurityStamp).SingleAsync(Ct));
}
