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

    private static string[] Strings(JsonElement element, string property) =>
        element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!).ToArray();
}
