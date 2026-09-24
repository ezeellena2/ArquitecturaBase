using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class DeleteUserEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleting_hides_the_user_but_keeps_the_row()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("borrar");
        var userId = await CreateAsync(client, tokens.AccessToken, email);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{userId}", tokens.AccessToken);
        using var detail = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);
        using var list = await client.GetWithTokenAsync($"/api/users?search={email}", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
        Assert.Equal(0, (await list.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        var deleted = await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters()
            .Where(user => user.Id == userId)
            .Select(user => new { user.IsDeleted, user.DeletedAtUtc, user.DeletedBy })
            .SingleAsync(Ct));
        Assert.True(deleted.IsDeleted);
        Assert.NotNull(deleted.DeletedAtUtc);
        Assert.NotNull(deleted.DeletedBy);
    }

    [Fact]
    public async Task Deleting_cuts_off_the_session_that_was_already_open()
    {
        using var victim = factory.CreateClient();
        var email = TestEmails.Unique("borrado");
        var tokens = await victim.LoginAsync(factory, email);
        var userId = await IdOfAsync(email);

        using var before = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);

        using var admin = factory.CreateClient();
        var adminTokens = await admin.LoginAsync(factory, ApiFactory.AdminEmail);
        using var delete = await admin.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{userId}", adminTokens.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var after = await victim.GetWithTokenAsync("/test/protected", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Nobody_deletes_their_own_account()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var myId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{myId}", tokens.AccessToken);
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
            HttpMethod.Delete, $"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deleting_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinborrado"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Delete, $"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken, language: "es");
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
}
