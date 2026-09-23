using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.IntegrationTests.Roles;

[Collection(ApiTestGroup.Name)]
public sealed class RolesEndpointsTests(ApiFactory factory)
{
    [Fact]
    public async Task The_list_shows_the_system_roles_with_their_permissions_and_user_count()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken);
        var roles = (await response.ReadJsonAsync()).EnumerateArray().ToArray();
        var admin = roles.Single(role => role.GetProperty("name").GetString() == SystemRoles.Admin);
        var user = roles.Single(role => role.GetProperty("name").GetString() == SystemRoles.User);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(admin.GetProperty("isSystemRole").GetBoolean());
        Assert.True(user.GetProperty("isSystemRole").GetBoolean());
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), Strings(admin, "permissions"));
        Assert.Empty(Strings(user, "permissions"));
        Assert.True(admin.GetProperty("userCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task The_permission_catalog_is_grouped_by_area_and_translated()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "es");
        var groups = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["users", "roles", "settings"], groups.Select(group => group.GetProperty("area").GetString()));
        Assert.Equal(["Usuarios", "Roles", "Configuración"], groups.Select(group => group.GetProperty("name").GetString()));

        var users = groups[0].GetProperty("permissions").EnumerateArray().ToArray();
        Assert.Equal(
            [Permissions.Users.Read, Permissions.Users.Manage],
            users.Select(permission => permission.GetProperty("code").GetString()));
        Assert.Equal(["Ver usuarios", "Administrar usuarios"], users.Select(permission => permission.GetProperty("name").GetString()));
        Assert.Equal(
            ["El listado y el detalle de cada cuenta.", "Dar de alta, editar, desactivar y eliminar cuentas."],
            users.Select(permission => permission.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task The_permission_catalog_is_also_in_english()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "en");
        var groups = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        Assert.Equal(["Users", "Roles", "Settings"], groups.Select(group => group.GetProperty("name").GetString()));

        var usersRead = groups[0].GetProperty("permissions").EnumerateArray().First();
        Assert.Equal(Permissions.Users.Read, usersRead.GetProperty("code").GetString());
        Assert.Equal("The list and the details of each account.", usersRead.GetProperty("description").GetString());
    }

    [Fact]
    public async Task The_role_list_requires_the_roles_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinroles"));

        using var response = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_permission_catalog_requires_the_roles_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sincatalogo"));

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
