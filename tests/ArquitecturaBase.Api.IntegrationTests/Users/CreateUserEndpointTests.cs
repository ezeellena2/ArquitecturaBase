using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class CreateUserEndpointTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_creates_a_user_with_the_requested_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("alta");

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/users",
            tokens.AccessToken,
            new { email, displayName = "Ana", roles = new[] { SystemRoles.Admin } });
        var userId = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([SystemRoles.Admin], await RolesOfAsync(userId));
        Assert.True(await factory.ExecuteDbContextAsync(db =>
            db.Users.Where(user => user.Id == userId).Select(user => user.IsActive && user.EmailConfirmed).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Without_roles_the_new_user_gets_the_user_role()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = TestEmails.Unique("sinroles") });
        var userId = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([SystemRoles.User], await RolesOfAsync(userId));
    }

    [Fact]
    public async Task An_email_with_an_active_account_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("repetido");
        using var first = await client.SendWithTokenAsync(HttpMethod.Post, "/api/users", tokens.AccessToken, new { email });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Ya existe una cuenta con ese correo.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task An_email_with_a_deleted_account_is_restored_with_the_new_roles()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var email = TestEmails.Unique("restaurar");
        using var first = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email, roles = new[] { SystemRoles.Admin } });
        var original = JsonSerializer.Deserialize<Guid>((await first.ReadJsonAsync()).GetRawText());

        // El borrado lógico, directo contra la base: el endpoint que lo hace es de la Tarea 10.
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Users.Remove(await db.Users.SingleAsync(user => user.Id == original, Ct));
            return await db.SaveChangesAsync(Ct);
        });

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email, displayName = "Vuelta", roles = new[] { SystemRoles.User } });
        var restored = JsonSerializer.Deserialize<Guid>((await response.ReadJsonAsync()).GetRawText());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(original, restored);
        Assert.Equal([SystemRoles.User], await RolesOfAsync(restored));
        Assert.Equal("Vuelta", await factory.ExecuteDbContextAsync(db =>
            db.Users.Where(user => user.Id == restored).Select(user => user.DisplayName).SingleAsync(Ct)));
    }

    [Fact]
    public async Task A_role_that_does_not_exist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/users",
            tokens.AccessToken,
            new { email = TestEmails.Unique("rolraro"), roles = new[] { "Inventado" } });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(RoleErrors.NotFoundCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_invalid_email_is_rejected_with_field_errors()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = "no-es-un-correo" }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Ingresá un correo válido.", problem.GetProperty("errors").GetProperty("email")[0].GetString());
    }

    [Fact]
    public async Task Creating_a_user_requires_the_users_manage_permission()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("sinpermiso"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", tokens.AccessToken, new { email = TestEmails.Unique("nada") }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("No tenés permiso para realizar esta acción.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public void The_display_name_limit_matches_the_column()
    {
        Assert.Equal(ApplicationUser.DisplayNameMaxLength, ValidationRules.DisplayNameMaxLength);
    }

    private async Task<string[]> RolesOfAsync(Guid userId) =>
        await factory.ExecuteDbContextAsync(db => db.Roles
            .Where(role => db.UserRoles.Any(userRole => userRole.UserId == userId && userRole.RoleId == role.Id))
            .Select(role => role.Name!)
            .OrderBy(name => name)
            .ToArrayAsync(Ct));
}
