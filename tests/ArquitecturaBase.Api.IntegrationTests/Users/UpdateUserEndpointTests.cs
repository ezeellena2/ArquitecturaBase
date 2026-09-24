using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class UpdateUserEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task The_detail_shows_the_profile_and_the_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("detalle");
        var userId = await CreateAsync(client, tokens.AccessToken, email, "Ana", [SystemRoles.User]);

        using var response = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);
        var user = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, user.GetProperty("email").GetString());
        Assert.Equal("Ana", user.GetProperty("displayName").GetString());
        Assert.True(user.GetProperty("isActive").GetBoolean());
        Assert.Equal([SystemRoles.User], Strings(user, "roles"));
        Assert.EndsWith("Z", user.GetProperty("createdAtUtc").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Editing_changes_the_name_and_replaces_the_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("editar"), "Ana", [SystemRoles.User]);

        using var update = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/users/{userId}",
            tokens.AccessToken,
            new { displayName = "Ana María", roles = new[] { SystemRoles.Admin } });
        using var read = await client.GetWithTokenAsync($"/api/users/{userId}", tokens.AccessToken);
        var user = await read.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Empty(await update.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Null(update.Content.Headers.ContentType);
        Assert.Equal("Ana María", user.GetProperty("displayName").GetString());
        Assert.Equal([SystemRoles.Admin], Strings(user, "roles"));
    }

    [Fact]
    public async Task Nobody_takes_the_admin_role_away_from_themselves()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var myId = (await me.ReadJsonAsync()).GetProperty("id").GetGuid();

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/users/{myId}",
            tokens.AccessToken,
            new { displayName = (string?)null, roles = new[] { SystemRoles.User } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.CannotModifySelfCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_role_that_does_not_exist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("rolraro2"), null, [SystemRoles.User]);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put, $"/api/users/{userId}", tokens.AccessToken, new { roles = new[] { "Inventado" } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RoleErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Editing_without_roles_is_rejected_with_field_errors()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateAsync(client, tokens.AccessToken, TestEmails.Unique("sinrol"), null, [SystemRoles.User]);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put, $"/api/users/{userId}", tokens.AccessToken, new { roles = Array.Empty<string>() }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Este campo es obligatorio.", problem.GetProperty("errors").GetProperty("roles")[0].GetString());
    }

    [Fact]
    public async Task Reading_a_user_requires_the_users_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinlectura"));

        using var response = await client.GetWithTokenAsync($"/api/users/{Guid.CreateVersion7()}", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Editing_a_user_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinedicion"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/users/{Guid.CreateVersion7()}",
            tokens.AccessToken,
            new { roles = new[] { SystemRoles.User } },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static async Task<Guid> CreateAsync(
        HttpClient client, string accessToken, string email, string? displayName, string[] roles)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", accessToken, new { email, displayName, roles });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
