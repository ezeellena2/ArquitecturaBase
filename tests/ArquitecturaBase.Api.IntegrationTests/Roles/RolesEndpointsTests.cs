using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Roles;

[Collection(ApiTestGroup.Name)]
public sealed class RolesEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.True(admin.GetProperty("isSystemRole").GetBoolean());
        Assert.True(user.GetProperty("isSystemRole").GetBoolean());
        Assert.Equal(factory.Services.GetRequiredService<IPermissionCatalog>().All.Order(StringComparer.Ordinal), Strings(admin, "permissions"));
        Assert.Empty(Strings(user, "permissions"));
        Assert.True(admin.GetProperty("userCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task The_paged_list_brings_one_page_of_roles_with_its_totals()
    {
        var prefix = "rolpag-" + Guid.NewGuid().ToString("N")[..8];
        await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await roles.CreateAsync(prefix + "-a", "Primero", [Permissions.Users.Read], Ct);
            await roles.CreateAsync(prefix + "-b", description: null, [], Ct);
            await roles.CreateAsync(prefix + "-c", description: null, [], Ct);
        });
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync(
            $"/api/roles/paged?search={prefix}&sort=-name&pageSize=2&page=1", tokens.AccessToken);
        var page = await response.ReadJsonAsync();
        var items = page.GetProperty("items").EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal([prefix + "-c", prefix + "-b"], items.Select(item => item.GetProperty("name").GetString()));
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
        Assert.False(page.GetProperty("hasPrevious").GetBoolean());
        // Cada ítem tiene la forma del catálogo y del detalle.
        Assert.False(items[0].GetProperty("isSystemRole").GetBoolean());
        Assert.Equal(0, items[0].GetProperty("userCount").GetInt32());
        Assert.Empty(Strings(items[0], "permissions"));
    }

    // OT1-C: the paged list sorts by every column with a datum. A role without a description goes last in both directions,
    // the counts are those of the row (people with the role, permissions it has) and equal values keep the usual order, the
    // name.
    [Fact]
    public async Task The_paged_list_sorts_by_description_people_and_permissions_with_empty_descriptions_last()
    {
        var prefix = "rolord-" + Guid.NewGuid().ToString("N")[..8];
        await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await roles.CreateAsync(prefix + "-a", "Beta", [Permissions.Users.Read], Ct);
            await roles.CreateAsync(prefix + "-b", "Alfa", [Permissions.Users.Read, Permissions.Roles.Read], Ct);
            await roles.CreateAsync(prefix + "-c", description: null, [], Ct);
        });
        // One person with «a», nobody with «b» and one with «c».
        await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            foreach (var role in new[] { prefix + "-a", prefix + "-c" })
            {
                var user = await users.CreateAsync(Email.Create(TestEmails.Unique("rolord")).Value, displayName: null, "es", Ct);
                await users.SetRolesAsync(user.Id, [role], Ct);
            }
        });
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        async Task<string[]> Sorted(string sort)
        {
            using var response = await client.GetWithTokenAsync($"/api/roles/paged?search={prefix}&sort={sort}", tokens.AccessToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return [.. (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray()
                .Select(item => item.GetProperty("name").GetString()![(prefix.Length + 1)..])];
        }

        Assert.Equal(["b", "a", "c"], await Sorted("description"));
        Assert.Equal(["a", "b", "c"], await Sorted("-description"));
        Assert.Equal(["b", "a", "c"], await Sorted("userCount"));
        Assert.Equal(["a", "c", "b"], await Sorted("-userCount"));
        Assert.Equal(["c", "a", "b"], await Sorted("permissions"));
        Assert.Equal(["b", "a", "c"], await Sorted("-permissions"));
    }

    [Fact]
    public async Task Sorting_the_paged_list_by_a_field_outside_the_whitelist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/roles/paged?sort=secret", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Validation.Failed", problem.GetProperty("code").GetString());
        Assert.Equal("No se puede ordenar por ese campo.", problem.GetProperty("errors").GetProperty("sort")[0].GetString());
    }

    [Fact]
    public async Task The_permission_catalog_is_grouped_by_area_and_translated()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "es");
        var groups = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(factory.Services.GetRequiredService<IPermissionCatalog>().All.Select(code => code.Split('.')[0]).Distinct(), groups.Select(group => group.GetProperty("area").GetString()));
        Assert.Equal(["Usuarios", "Roles", "Configuración"], groups.Take(3).Select(group => group.GetProperty("name").GetString()));

        var users = groups[0].GetProperty("permissions").EnumerateArray().ToArray();
        Assert.Equal(
            [Permissions.Users.Read, Permissions.Users.Manage],
            users.Select(permission => permission.GetProperty("code").GetString()));
        Assert.Equal(["Ver usuarios", "Administrar usuarios"], users.Select(permission => permission.GetProperty("name").GetString()));
        // Lo que incluye cada uno: «Administrar usuarios» no sirve sin «Ver usuarios».
        Assert.Equal(
            [[], [Permissions.Users.Read]],
            users.Select(permission => Strings(permission, "includes")));
        Assert.Equal(
            ["El listado y el detalle de cada cuenta.", "Dar de alta, editar, desactivar y eliminar cuentas. Quien edita sólo da o quita roles con permisos que tiene, y a una cuenta con permisos que no tiene no le cambia el correo ni el número, ni la desactiva, reactiva o borra."],
            users.Select(permission => permission.GetProperty("description").GetString()));
    }

    [Fact]
    public async Task The_permission_catalog_is_also_in_english()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/permissions", tokens.AccessToken, language: "en");
        var groups = (await response.ReadJsonAsync()).EnumerateArray().ToArray();

        Assert.Equal(["Users", "Roles", "Settings"], groups.Take(3).Select(group => group.GetProperty("name").GetString()));

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
    public async Task The_paged_role_list_requires_the_roles_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinpaginado"));

        using var response = await client.GetWithTokenAsync("/api/roles/paged", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task The_role_detail_requires_the_roles_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sindetalle"));

        // La autorización corta antes de buscar el rol: el id no necesita existir.
        using var response = await client.GetWithTokenAsync(
            $"/api/roles/{Guid.CreateVersion7()}", tokens.AccessToken, language: "es");
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
