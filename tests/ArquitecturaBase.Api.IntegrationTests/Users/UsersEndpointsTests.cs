using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class UsersEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Me_returns_the_profile_roles_and_permissions_of_the_token_owner()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("me");
        var tokens = await client.LoginAsync(factory, email);

        using var response = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.Equal("es", me.GetProperty("culture").GetString());
        Assert.Equal("America/Argentina/Buenos_Aires", me.GetProperty("timeZoneId").GetString());
        Assert.Equal(["User"], Strings(me, "roles"));
        Assert.Empty(Strings(me, "permissions"));
    }

    [Fact]
    public async Task Admin_gets_every_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await response.ReadJsonAsync();

        Assert.Contains("Admin", Strings(me, "roles"));
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), Strings(me, "permissions"));
    }

    [Fact]
    public async Task Me_without_a_token_returns_a_401_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/api/me");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Users_list_requires_the_users_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("nolist"));

        using var response = await client.GetWithTokenAsync("/api/users", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Admin_lists_users_with_search_sort_and_paging()
    {
        var prefix = TestEmails.Unique("list").Split('@')[0];
        await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            await identity.CreateAsync(Email.Create(prefix + "-a@example.com").Value, "Ana", "es", Ct);
            await identity.CreateAsync(Email.Create(prefix + "-b@example.com").Value, "Beto", "es", Ct);
            return true;
        });
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users?search={prefix}&sort=-email&pageSize=1", tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(prefix + "-b@example.com", Assert.Single(page.GetProperty("items").EnumerateArray()).GetProperty("email").GetString());
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.True(page.GetProperty("hasNext").GetBoolean());
    }

    [Fact]
    public async Task Sorting_by_a_field_outside_the_whitelist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/users?sort=passwordHash", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No se puede ordenar por ese campo.", problem.GetProperty("errors").GetProperty("sort")[0].GetString());
    }

    [Fact]
    public async Task Filtering_by_status_brings_only_the_matching_users()
    {
        var prefix = TestEmails.Unique("bystatus").Split('@')[0];
        await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            await identity.CreateAsync(Email.Create(prefix + "-on@example.com").Value, "Activa", "es", Ct);
            var off = await identity.CreateAsync(Email.Create(prefix + "-off@example.com").Value, "Inactivo", "es", Ct);
            await identity.SetActiveAsync(off.Id, isActive: false, Ct);
            return true;
        });
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users?search={prefix}&isActive=false", tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([prefix + "-off@example.com"], Emails(page));
    }

    [Fact]
    public async Task Filtering_by_role_brings_only_the_users_that_have_it()
    {
        var prefix = TestEmails.Unique("byrole").Split('@')[0];
        await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            await identity.CreateAsync(Email.Create(prefix + "-plain@example.com").Value, "Sin rol", "es", Ct);
            var boss = await identity.CreateAsync(Email.Create(prefix + "-boss@example.com").Value, "Con rol", "es", Ct);
            await identity.SetRolesAsync(boss.Id, [SystemRoles.Admin], Ct);
            return true;
        });
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        // En minúsculas a propósito: el filtro compara por el nombre normalizado, que es el que tiene índice.
        using var response = await client.GetWithTokenAsync($"/api/users?search={prefix}&role=admin", tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal([prefix + "-boss@example.com"], Emails(page));
    }

    [Fact]
    public async Task A_role_that_does_not_exist_returns_an_empty_list_and_not_a_400()
    {
        // Decisión: el código de respuesta no cuenta qué roles existen. Un rol desconocido no tiene a nadie.
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/users?role=NoExisteEsteRol", tokens.AccessToken);
        var page = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(page.GetProperty("items").EnumerateArray());
        Assert.Equal(0, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task A_role_filter_with_no_value_is_a_400()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/users?role=", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Este campo es obligatorio.", problem.GetProperty("errors").GetProperty("role")[0].GetString());
    }

    [Fact]
    public async Task The_three_filters_intersect()
    {
        var prefix = TestEmails.Unique("trio").Split('@')[0];
        await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            // Los tres viejos: uno que solo falla por la fecha, uno por el estado y uno por el rol.
            var oldBoss = await identity.CreateAsync(Email.Create(prefix + "-old@example.com").Value, "Viejo", "es", Ct);
            await identity.SetRolesAsync(oldBoss.Id, [SystemRoles.Admin], Ct);
            return true;
        });

        factory.Clock.Advance(TimeSpan.FromDays(30));

        await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            var off = await identity.CreateAsync(Email.Create(prefix + "-off@example.com").Value, "Apagado", "es", Ct);
            await identity.SetRolesAsync(off.Id, [SystemRoles.Admin], Ct);
            await identity.SetActiveAsync(off.Id, isActive: false, Ct);
            await identity.CreateAsync(Email.Create(prefix + "-plain@example.com").Value, "Sin rol", "es", Ct);
            var match = await identity.CreateAsync(Email.Create(prefix + "-ok@example.com").Value, "El único", "es", Ct);
            await identity.SetRolesAsync(match.Id, [SystemRoles.Admin], Ct);
            return true;
        });

        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var byAge = await client.GetWithTokenAsync(
            $"/api/users?search={prefix}&createdWithinDays=7&sort=email", tokens.AccessToken);
        using var byAll = await client.GetWithTokenAsync(
            $"/api/users?search={prefix}&isActive=true&role=Admin&createdWithinDays=7", tokens.AccessToken);

        // La fecha sola deja fuera al viejo y a nadie más.
        Assert.Equal(
            [prefix + "-off@example.com", prefix + "-ok@example.com", prefix + "-plain@example.com"],
            Emails(await byAge.ReadJsonAsync()));
        // Los tres juntos se intersecan: activo, con el rol y reciente.
        Assert.Equal([prefix + "-ok@example.com"], Emails(await byAll.ReadJsonAsync()));
    }

    [Fact]
    public async Task Filter_counts_describe_the_same_list_the_filters_would_bring()
    {
        var prefix = await CreateCountsCohortAsync();
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users/filter-counts?search={prefix}", tokens.AccessToken);
        var counts = await response.ReadJsonAsync();
        var status = counts.GetProperty("status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, status.GetProperty("all").GetInt32());
        Assert.Equal(1, status.GetProperty("active").GetInt32());
        Assert.Equal(1, status.GetProperty("inactive").GetInt32());
        Assert.Equal(
            status.GetProperty("all").GetInt32(),
            status.GetProperty("active").GetInt32() + status.GetProperty("inactive").GetInt32());
        // Los dos son del cohorte y los dos son de ahora, así que el tramo más corto los trae a los dos.
        Assert.Equal(2, RoleCount(counts, SystemRoles.Admin));
        Assert.Equal(2, counts.GetProperty("createdWithin").EnumerateArray()
            .Single(tramo => tramo.GetProperty("days").GetInt32() == 7).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Roles_that_nobody_has_are_listed_with_zero()
    {
        // La opción con cero se muestra apagada, y para eso hay que saber que existe. Los dos del cohorte
        // tienen Admin y ninguno User.
        var prefix = await CreateCountsCohortAsync();
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users/filter-counts?search={prefix}", tokens.AccessToken);
        var counts = await response.ReadJsonAsync();

        Assert.Equal(0, RoleCount(counts, SystemRoles.User));
    }

    [Fact]
    public async Task The_role_counts_follow_the_status_that_is_already_filtered()
    {
        // Esta es la parte que hace que los números no mientan: con "solo activos" puesto, el número de
        // "Admin" es cuántos activos quedarían al elegir Admin, no cuántos Admin hay en total.
        var prefix = await CreateCountsCohortAsync();
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync(
            $"/api/users/filter-counts?search={prefix}&isActive=true", tokens.AccessToken);
        var counts = await response.ReadJsonAsync();

        Assert.Equal(1, RoleCount(counts, SystemRoles.Admin));
    }

    [Fact]
    public async Task The_status_counts_ignore_the_status_filter_itself()
    {
        // Y esta es la otra mitad: con "solo activos" puesto, "Inactivos" tiene que seguir diciendo cuántos
        // hay del otro lado. Contado con su propio filtro diría 0 y el filtro parecería no llevar a ningún lado.
        var prefix = await CreateCountsCohortAsync();
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync(
            $"/api/users/filter-counts?search={prefix}&isActive=true", tokens.AccessToken);
        var status = (await response.ReadJsonAsync()).GetProperty("status");

        Assert.Equal(2, status.GetProperty("all").GetInt32());
        Assert.Equal(1, status.GetProperty("inactive").GetInt32());
    }

    [Fact]
    public async Task Filter_counts_require_the_users_read_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("nocounts"));

        using var response = await client.GetWithTokenAsync("/api/users/filter-counts", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
    }

    /// <summary>Dos usuarios con el rol Admin y ninguno con User: uno activo y el otro no.</summary>
    private async Task<string> CreateCountsCohortAsync()
    {
        var prefix = TestEmails.Unique("counts").Split('@')[0];
        await factory.ExecuteScopeAsync(async services =>
        {
            var identity = services.GetRequiredService<IIdentityService>();
            var on = await identity.CreateAsync(Email.Create(prefix + "-on@example.com").Value, "Activa", "es", Ct);
            await identity.SetRolesAsync(on.Id, [SystemRoles.Admin], Ct);
            var off = await identity.CreateAsync(Email.Create(prefix + "-off@example.com").Value, "Apagado", "es", Ct);
            await identity.SetRolesAsync(off.Id, [SystemRoles.Admin], Ct);
            await identity.SetActiveAsync(off.Id, isActive: false, Ct);
            return true;
        });

        return prefix;
    }

    private static int RoleCount(JsonElement counts, string role) =>
        counts.GetProperty("roles").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == role)
            .GetProperty("count").GetInt32();

    private static string[] Emails(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("email").GetString()!).ToArray();

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
