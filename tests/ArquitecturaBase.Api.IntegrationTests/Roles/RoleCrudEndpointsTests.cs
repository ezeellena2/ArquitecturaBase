using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.IntegrationTests.Roles;

[Collection(ApiTestGroup.Name)]
public sealed class RoleCrudEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task Admin_creates_a_role_with_its_permissions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = UniqueName("lectores");

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/roles",
            tokens.AccessToken,
            new { name, description = "Solo lectura", permissions = new[] { Permissions.Users.Read } });
        var roleId = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
        var role = await FindAsync(client, tokens.AccessToken, roleId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(name, role.GetProperty("name").GetString());
        Assert.Equal("Solo lectura", role.GetProperty("description").GetString());
        Assert.False(role.GetProperty("isSystemRole").GetBoolean());
        Assert.Equal(0, role.GetProperty("userCount").GetInt32());
        Assert.Equal([Permissions.Users.Read], Strings(role, "permissions"));
    }

    [Fact]
    public async Task A_repeated_name_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = UniqueName("repetido");
        await CreateAsync(client, tokens.AccessToken, name, []);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/roles", tokens.AccessToken, new { name, permissions = Array.Empty<string>() }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RoleErrors.AlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ya existe un rol con ese nombre.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_permission_outside_the_catalog_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/roles",
            tokens.AccessToken,
            new { name = UniqueName("raro"), permissions = new[] { "inventado.total" } },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ese permiso no existe.", problem.GetProperty("errors").GetProperty("permissions")[0].GetString());
    }

    [Fact]
    public async Task Editing_changes_the_name_the_description_and_the_permissions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var roleId = await CreateAsync(client, tokens.AccessToken, UniqueName("editar"), [Permissions.Users.Read]);
        var newName = UniqueName("editado");

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{roleId}",
            tokens.AccessToken,
            new { name = newName, description = "Cambiada", permissions = new[] { Permissions.Roles.Read } });
        var role = await FindAsync(client, tokens.AccessToken, roleId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newName, role.GetProperty("name").GetString());
        Assert.Equal("Cambiada", role.GetProperty("description").GetString());
        Assert.Equal([Permissions.Roles.Read], Strings(role, "permissions"));
    }

    [Fact]
    public async Task Changing_the_permissions_of_a_role_takes_effect_for_its_users_right_away()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var roleId = await CreateAsync(client, tokens.AccessToken, UniqueName("cache"), [Permissions.Users.Read]);

        // Una persona con ese rol entra y usa el permiso: acá se llena el caché de PermissionService.
        var email = TestEmails.Unique("cache");
        using var create = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email, roles = new[] { RoleNameOf(await FindAsync(client, tokens.AccessToken, roleId)) } });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var member = factory.CreateClient();
        var memberTokens = await member.LoginAsync(factory, email);
        using var before = await member.GetWithTokenAsync("/api/me", memberTokens.AccessToken);
        Assert.Equal([Permissions.Users.Read], Strings(await before.ReadJsonAsync(), "permissions"));

        using var update = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{roleId}",
            tokens.AccessToken,
            new { name = RoleNameOf(await FindAsync(client, tokens.AccessToken, roleId)), permissions = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        using var after = await member.GetWithTokenAsync("/api/me", memberTokens.AccessToken);

        Assert.Empty(Strings(await after.ReadJsonAsync(), "permissions"));
    }

    [Fact]
    public async Task The_admin_role_cannot_be_renamed()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var admin = await FindByNameAsync(client, tokens.AccessToken, SystemRoles.Admin);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{admin.GetProperty("id").GetGuid()}",
            tokens.AccessToken,
            new { name = "Superadmin", permissions = Strings(admin, "permissions") });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RoleErrors.SystemRoleCannotChangeCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_admin_role_does_not_lose_permissions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var admin = await FindByNameAsync(client, tokens.AccessToken, SystemRoles.Admin);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            $"/api/roles/{admin.GetProperty("id").GetGuid()}",
            tokens.AccessToken,
            new { name = SystemRoles.Admin, permissions = new[] { Permissions.Users.Read } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RoleErrors.SystemRoleCannotChangeCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_system_role_cannot_be_deleted()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var user = await FindByNameAsync(client, tokens.AccessToken, SystemRoles.User);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Delete, $"/api/roles/{user.GetProperty("id").GetGuid()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(RoleErrors.SystemRoleCannotChangeCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_role_with_users_cannot_be_deleted_and_the_error_says_how_many()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = UniqueName("conusuarios");
        var roleId = await CreateAsync(client, tokens.AccessToken, name, []);
        using var create = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = TestEmails.Unique("miembro"), roles = new[] { name } });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/roles/{roleId}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(RoleErrors.HasUsersCode, problem.GetProperty("code").GetString());
        Assert.Equal(1, problem.GetProperty(RoleErrors.UserCountKey).GetInt32());
    }

    [Fact]
    public async Task A_role_without_users_is_deleted()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var roleId = await CreateAsync(client, tokens.AccessToken, UniqueName("descartable"), []);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/roles/{roleId}", tokens.AccessToken);
        using var list = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(
            (await list.ReadJsonAsync()).EnumerateArray(),
            role => role.GetProperty("id").GetGuid() == roleId);
    }

    [Fact]
    public async Task An_id_that_does_not_exist_is_not_found()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Delete, $"/api/roles/{Guid.CreateVersion7()}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RoleErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Managing_roles_requires_the_roles_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("singestion"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/roles",
            tokens.AccessToken,
            new { name = UniqueName("prohibido"), permissions = Array.Empty<string>() },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static string UniqueName(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];

    private static string RoleNameOf(JsonElement role) => role.GetProperty("name").GetString()!;

    private static async Task<Guid> CreateAsync(HttpClient client, string accessToken, string name, string[] permissions)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/roles", accessToken, new { name, permissions });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());
    }

    private static async Task<JsonElement> FindAsync(HttpClient client, string accessToken, Guid roleId)
    {
        using var response = await client.GetWithTokenAsync("/api/roles", accessToken);

        return (await response.ReadJsonAsync()).EnumerateArray().Single(role => role.GetProperty("id").GetGuid() == roleId);
    }

    private static async Task<JsonElement> FindByNameAsync(HttpClient client, string accessToken, string name)
    {
        using var response = await client.GetWithTokenAsync("/api/roles", accessToken);

        return (await response.ReadJsonAsync()).EnumerateArray().Single(role => role.GetProperty("name").GetString() == name);
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
