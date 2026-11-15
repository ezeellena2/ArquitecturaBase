using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Nadie da lo que no tiene (N1 de la revisión del núcleo, 2026-11-14): una cuenta con sólo «Administrar usuarios» o sólo
/// «Administrar roles» no llega a ser administrador ni toma la cuenta de uno, y Admin sigue pudiendo todo. Y el
/// administrador inicial de Seed:AdminEmail no pierde Admin ni el correo desde la pantalla (N25). Lo que necesita otro
/// administrador inicial corre en una Api aparte sobre la misma base (WithWebHostBuilder, con otro Seed:AdminEmail): el de
/// ApiFactory lo usan todos los tests y no se puede borrar ni cambiar.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class DelegatedAdministrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_only_manage_users_nobody_ticks_admin()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        using var helpDesk = await SignInWithRoleAsync(admin, "mesa", [Permissions.Users.Read, Permissions.Users.Manage]);
        var otherId = await admin.CreateOkAsync(new { email = TestEmails.Unique("otro"), roles = new[] { SystemRoles.User } });

        using var self = await helpDesk.SendAsync(
            HttpMethod.Put, $"/api/users/{helpDesk.UserId}", new { roles = new[] { helpDesk.Role, SystemRoles.Admin } });
        using var other = await helpDesk.SendAsync(
            HttpMethod.Put, $"/api/users/{otherId}", new { roles = new[] { SystemRoles.Admin } });
        using var created = await helpDesk.SendAsync(
            HttpMethod.Post, "/api/users", new { email = TestEmails.Unique("nuevo"), roles = new[] { SystemRoles.Admin } });

        await AssertProblemAsync(self, HttpStatusCode.Forbidden, UserErrors.RolesNotHeldCode);
        await AssertProblemAsync(other, HttpStatusCode.Forbidden, UserErrors.RolesNotHeldCode);
        await AssertProblemAsync(created, HttpStatusCode.Forbidden, UserErrors.RolesNotHeldCode);
        Assert.Equal([helpDesk.Role], await RolesOfAsync(admin, helpDesk.UserId));
        Assert.Equal([SystemRoles.User], await RolesOfAsync(admin, otherId));
    }

    [Fact]
    public async Task With_only_manage_users_an_administrator_is_out_of_reach()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        using var helpDesk = await SignInWithRoleAsync(admin, "mesa", [Permissions.Users.Read, Permissions.Users.Manage]);
        var adminEmail = TestEmails.Unique("admin2");
        var otherAdminId = await admin.CreateOkAsync(new { email = adminEmail, roles = new[] { SystemRoles.Admin } });
        string[] roles = [SystemRoles.Admin];

        using var email = await helpDesk.SendAsync(
            HttpMethod.Put, $"/api/users/{otherAdminId}", new { roles, email = TestEmails.Unique("lucia") });
        using var phone = await helpDesk.SendAsync(
            HttpMethod.Put, $"/api/users/{otherAdminId}", new { roles, phone = AdminUsersApi.PhoneField(TestPhones.Unique()) });
        using var deactivate = await helpDesk.SendAsync(HttpMethod.Post, $"/api/users/{otherAdminId}/deactivate");
        using var delete = await helpDesk.SendAsync(HttpMethod.Delete, $"/api/users/{otherAdminId}");

        await AssertProblemAsync(email, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        await AssertProblemAsync(phone, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        await AssertProblemAsync(deactivate, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        await AssertProblemAsync(delete, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        var detail = await DetailAsync(admin, otherAdminId);
        Assert.Equal(adminEmail, detail.GetProperty("email").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("phoneNumber").ValueKind);
        Assert.True(detail.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task With_only_manage_users_an_administrator_keeps_its_number_and_stays_deactivated()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        using var helpDesk = await SignInWithRoleAsync(admin, "mesa", [Permissions.Users.Read, Permissions.Users.Manage]);
        var phone = TestPhones.Unique();
        var otherAdminId = await admin.CreateOkAsync(new
        {
            email = TestEmails.Unique("admin3"),
            phone = AdminUsersApi.PhoneField(phone),
            roles = new[] { SystemRoles.Admin },
        });
        using var deactivated = await admin.Client.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{otherAdminId}/deactivate", admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);

        using var unlink = await helpDesk.SendAsync(HttpMethod.Delete, $"/api/users/{otherAdminId}/whatsapp");
        using var activate = await helpDesk.SendAsync(HttpMethod.Post, $"/api/users/{otherAdminId}/activate");

        await AssertProblemAsync(unlink, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        await AssertProblemAsync(activate, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        var detail = await DetailAsync(admin, otherAdminId);
        Assert.Equal(phone.Value, detail.GetProperty("phoneNumber").GetString());
        Assert.False(detail.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task With_manage_users_and_manage_roles_together_nobody_becomes_an_administrator()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        string[] held = [Permissions.Users.Read, Permissions.Users.Manage, Permissions.Roles.Read, Permissions.Roles.Manage];
        using var both = await SignInWithRoleAsync(admin, "ambos", held);
        var userRoleId = (await FindRoleByNameAsync(admin, SystemRoles.User)).GetProperty("id").GetGuid();
        var otherAdminId = await admin.CreateOkAsync(new { email = TestEmails.Unique("admin4"), roles = new[] { SystemRoles.Admin } });

        // Por roles: sumarse un permiso, crear un rol con uno que no tiene o cambiar User.
        string[] withSettings = [.. held, Permissions.Settings.Manage];
        using var own = await both.SendAsync(
            HttpMethod.Put, $"/api/roles/{both.RoleId}", new { name = both.Role, permissions = withSettings });
        using var createdRole = await both.SendAsync(
            HttpMethod.Post, "/api/roles", new { name = UniqueName("ajustes"), permissions = new[] { Permissions.Settings.Manage } });
        using var toUser = await both.SendAsync(
            HttpMethod.Put, $"/api/roles/{userRoleId}", new { name = SystemRoles.User, permissions = held });

        // Por cuentas: darse Admin o tomar la cuenta de un administrador.
        using var self = await both.SendAsync(
            HttpMethod.Put, $"/api/users/{both.UserId}", new { roles = new[] { both.Role, SystemRoles.Admin } });
        using var takeOver = await both.SendAsync(
            HttpMethod.Put, $"/api/users/{otherAdminId}",
            new { roles = new[] { SystemRoles.Admin }, email = TestEmails.Unique("toma") });

        // Lo suyo sí lo delega: un rol con permisos que tiene, dado a una cuenta nueva.
        var lesser = UniqueName("lectura");
        using var lesserRole = await both.SendAsync(
            HttpMethod.Post, "/api/roles", new { name = lesser, permissions = new[] { Permissions.Users.Read } });
        using var delegated = await both.SendAsync(
            HttpMethod.Post, "/api/users", new { email = TestEmails.Unique("delegada"), roles = new[] { lesser } });

        await AssertProblemAsync(own, HttpStatusCode.Forbidden, RoleErrors.PermissionsNotHeldCode);
        await AssertProblemAsync(createdRole, HttpStatusCode.Forbidden, RoleErrors.PermissionsNotHeldCode);
        await AssertProblemAsync(toUser, HttpStatusCode.Forbidden, RoleErrors.UserRoleAdminOnlyCode);
        await AssertProblemAsync(self, HttpStatusCode.Forbidden, UserErrors.RolesNotHeldCode);
        await AssertProblemAsync(takeOver, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        Assert.Equal(HttpStatusCode.Created, lesserRole.StatusCode);
        Assert.Equal(HttpStatusCode.Created, delegated.StatusCode);
        Assert.Equal([both.Role], await RolesOfAsync(admin, both.UserId));
        Assert.Equal(held.Order(StringComparer.Ordinal), Strings(await FindRoleAsync(admin, both.RoleId), "permissions"));
    }

    [Fact]
    public async Task With_only_manage_users_the_deleted_initial_administrator_is_not_restored_by_its_number()
    {
        // La cuenta borrada del administrador inicial, con su número: restaurarla trae el correo de Seed:AdminEmail.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var seedEmail = TestEmails.Unique("inicial");
        var phone = TestPhones.Unique();
        var initialId = await admin.CreateOkAsync(new
        {
            email = seedEmail,
            phone = AdminUsersApi.PhoneField(phone),
            roles = new[] { SystemRoles.Admin },
        });
        using var deleted = await admin.Client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{initialId}", admin.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("Seed:AdminEmail", seedEmail));
        using var helpDesk = await SignInWithRoleAsync(admin, "mesa", [Permissions.Users.Read, Permissions.Users.Manage], api);

        using var restored = await helpDesk.SendAsync(
            HttpMethod.Post, "/api/users", new { phone = AdminUsersApi.PhoneField(phone), roles = new[] { SystemRoles.User } });

        await AssertProblemAsync(restored, HttpStatusCode.Forbidden, UserErrors.RolesNotHeldCode);
        using (var gone = await admin.Client.GetWithTokenAsync($"/api/users/{initialId}", admin.AccessToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }

        // Restaurada con sólo User por quien sí puede, sigue fuera de su alcance: el arranque le devolvería Admin.
        Assert.Equal(initialId, await admin.CreateOkAsync(new { email = seedEmail, roles = new[] { SystemRoles.User } }));
        using var ownNumber = await helpDesk.SendAsync(
            HttpMethod.Put, $"/api/users/{initialId}",
            new { roles = new[] { SystemRoles.User }, phone = AdminUsersApi.PhoneField(TestPhones.Unique()) });

        await AssertProblemAsync(ownNumber, HttpStatusCode.Forbidden, UserErrors.HasMorePermissionsCode);
        Assert.Equal(phone.Value, (await DetailAsync(admin, initialId)).GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task With_only_manage_users_user_still_goes_to_new_accounts_when_it_has_more_permissions()
    {
        // User lo recibe toda cuenta nueva y sólo lo edita Admin: sus permisos cuentan como de quien pide.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        using var helpDesk = await SignInWithRoleAsync(admin, "mesa", [Permissions.Users.Read, Permissions.Users.Manage]);
        var user = await FindRoleByNameAsync(admin, SystemRoles.User);
        var userRoleId = user.GetProperty("id").GetGuid();
        var original = Strings(user, "permissions");

        try
        {
            using var changed = await admin.Client.SendWithTokenAsync(
                HttpMethod.Put, $"/api/roles/{userRoleId}", admin.AccessToken,
                new { name = SystemRoles.User, permissions = new[] { Permissions.Settings.Manage } });
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

            using var created = await helpDesk.SendAsync(
                HttpMethod.Post, "/api/users", new { email = TestEmails.Unique("con-user"), roles = new[] { SystemRoles.User } });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var createdId = JsonSerializer.Deserialize<Guid>((await created.ReadJsonAsync()).GetRawText());
            using var edited = await helpDesk.SendAsync(
                HttpMethod.Put, $"/api/users/{createdId}",
                new { roles = new[] { SystemRoles.User }, email = TestEmails.Unique("con-user-2") });

            Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        }
        finally
        {
            using var restored = await admin.Client.SendWithTokenAsync(
                HttpMethod.Put, $"/api/roles/{userRoleId}", admin.AccessToken,
                new { name = SystemRoles.User, permissions = original });
            Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        }
    }

    [Fact]
    public async Task With_only_manage_users_an_account_with_fewer_permissions_is_managed_as_before()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        using var helpDesk = await SignInWithRoleAsync(admin, "mesa", [Permissions.Users.Read, Permissions.Users.Manage]);
        var newEmail = TestEmails.Unique("nuevo-correo");

        using var created = await helpDesk.SendAsync(
            HttpMethod.Post, "/api/users", new { email = TestEmails.Unique("alta"), roles = new[] { SystemRoles.User } });
        var userId = JsonSerializer.Deserialize<Guid>((await created.ReadJsonAsync()).GetRawText());
        using var edited = await helpDesk.SendAsync(
            HttpMethod.Put, $"/api/users/{userId}", new { roles = new[] { SystemRoles.User }, email = newEmail });
        using var deactivated = await helpDesk.SendAsync(HttpMethod.Post, $"/api/users/{userId}/deactivate");

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, edited.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deactivated.StatusCode);
        Assert.Equal(newEmail, (await DetailAsync(admin, userId)).GetProperty("email").GetString());
    }

    [Fact]
    public async Task With_only_manage_roles_nobody_gives_permissions_they_do_not_have()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        string[] held = [Permissions.Roles.Read, Permissions.Roles.Manage];
        using var roleManager = await SignInWithRoleAsync(admin, "roles", held);
        var user = await FindRoleByNameAsync(admin, SystemRoles.User);
        var userPermissions = Strings(user, "permissions");

        string[] withUsers = [.. held, Permissions.Users.Manage];
        using var own = await roleManager.SendAsync(
            HttpMethod.Put, $"/api/roles/{roleManager.RoleId}", new { name = roleManager.Role, permissions = withUsers });
        using var toUser = await roleManager.SendAsync(
            HttpMethod.Put,
            $"/api/roles/{user.GetProperty("id").GetGuid()}",
            new { name = SystemRoles.User, permissions = held });
        using var created = await roleManager.SendAsync(
            HttpMethod.Post, "/api/roles", new { name = UniqueName("todo"), permissions = new[] { Permissions.Users.Manage } });
        using var allowed = await roleManager.SendAsync(
            HttpMethod.Post, "/api/roles", new { name = UniqueName("lectura"), permissions = new[] { Permissions.Roles.Read } });

        await AssertProblemAsync(own, HttpStatusCode.Forbidden, RoleErrors.PermissionsNotHeldCode);
        await AssertProblemAsync(toUser, HttpStatusCode.Forbidden, RoleErrors.UserRoleAdminOnlyCode);
        await AssertProblemAsync(created, HttpStatusCode.Forbidden, RoleErrors.PermissionsNotHeldCode);
        Assert.Equal(HttpStatusCode.Created, allowed.StatusCode);
        Assert.Equal(held.Order(StringComparer.Ordinal), Strings(await FindRoleAsync(admin, roleManager.RoleId), "permissions"));
        Assert.Equal(userPermissions, Strings(await FindRoleByNameAsync(admin, SystemRoles.User), "permissions"));
    }

    [Fact]
    public async Task An_administrator_changes_the_permissions_of_user_and_gives_any_role()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var user = await FindRoleByNameAsync(admin, SystemRoles.User);
        var userId = user.GetProperty("id").GetGuid();
        var original = Strings(user, "permissions");

        try
        {
            using var changed = await admin.Client.SendWithTokenAsync(
                HttpMethod.Put, $"/api/roles/{userId}", admin.AccessToken,
                new { name = SystemRoles.User, permissions = new[] { Permissions.Users.Read } });

            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
            Assert.Equal([Permissions.Users.Read], Strings(await FindRoleAsync(admin, userId), "permissions"));
        }
        finally
        {
            // User lo recibe toda cuenta nueva: los demás tests lo esperan como estaba.
            using var restored = await admin.Client.SendWithTokenAsync(
                HttpMethod.Put, $"/api/roles/{userId}", admin.AccessToken,
                new { name = SystemRoles.User, permissions = original });
            Assert.Equal(HttpStatusCode.NoContent, restored.StatusCode);
        }

        var otherId = await admin.CreateOkAsync(new { email = TestEmails.Unique("nuevo-admin"), roles = new[] { SystemRoles.Admin } });
        Assert.Equal([SystemRoles.Admin], await RolesOfAsync(admin, otherId));
    }

    [Fact]
    public async Task The_initial_administrator_keeps_admin_and_its_email()
    {
        // Otro administrador, con todos los permisos: lo frena la configuración, no los permisos.
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var otherAdminEmail = TestEmails.Unique("otro-admin");
        await admin.CreateOkAsync(new { email = otherAdminEmail, roles = new[] { SystemRoles.Admin } });
        using var otherClient = factory.CreateClient();
        var otherTokens = await otherClient.LoginAsync(factory, otherAdminEmail);

        using var takeAdmin = await otherClient.SendWithTokenAsync(
            HttpMethod.Put, $"/api/users/{admin.AdminId}", otherTokens.AccessToken, new { roles = new[] { SystemRoles.User } });
        using var changeEmail = await otherClient.SendWithTokenAsync(
            HttpMethod.Put, $"/api/users/{admin.AdminId}", otherTokens.AccessToken,
            new { roles = new[] { SystemRoles.Admin }, email = TestEmails.Unique("reemplazo") });

        var problem = await AssertProblemAsync(takeAdmin, HttpStatusCode.Conflict, UserErrors.InitialAdminCode);
        await AssertProblemAsync(changeEmail, HttpStatusCode.Conflict, UserErrors.InitialAdminCode);
        Assert.Contains("Seed:AdminEmail", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal([SystemRoles.Admin], await RolesOfAsync(admin, admin.AdminId));
        Assert.Equal(ApiFactory.AdminEmail, (await DetailAsync(admin, admin.AdminId)).GetProperty("email").GetString());
    }

    [Fact]
    public async Task The_initial_administrator_does_not_change_its_email_from_the_profile()
    {
        var seedEmail = TestEmails.Unique("inicial-perfil");
        var account = await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            Email.Create(seedEmail).Value, phone: null, phoneConfirmed: false, displayName: null, "es", Ct));
        var userId = account.Id.ToString("D", CultureInfo.InvariantCulture);
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("Seed:AdminEmail", seedEmail));
        using var client = api.CreateClient();
        var newEmail = TestEmails.Unique("otro-correo");

        using var ask = await client.SendAsync(
            HttpMethod.Post, "/api/me/email/code", JsonContent.Create(new { email = newEmail }), userId: userId);
        Assert.Equal(HttpStatusCode.Accepted, ask.StatusCode);
        var code = CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(newEmail));
        using var confirm = await client.SendAsync(
            HttpMethod.Put, "/api/me/email", JsonContent.Create(new { email = newEmail, code }), userId: userId);

        await AssertProblemAsync(confirm, HttpStatusCode.Conflict, UserErrors.InitialAdminCode);
        var stored = await factory.ExecuteScopeAsync(services =>
            services.GetRequiredService<IUserReader>().FindByIdAsync(account.Id, Ct));
        Assert.Equal(seedEmail, stored?.Email);
    }

    // Un rol con esos permisos, creado por el administrador, y una cuenta con sólo ese rol que ya entró, en la Api de
    // ApiFactory o en <paramref name="api"/>.
    private async Task<Member> SignInWithRoleAsync(
        AdminUsersApi admin, string prefix, string[] permissions, WebApplicationFactory<Program>? api = null)
    {
        var role = UniqueName(prefix);
        using var createdRole = await admin.Client.SendWithTokenAsync(
            HttpMethod.Post, "/api/roles", admin.AccessToken, new { name = role, permissions });
        Assert.Equal(HttpStatusCode.Created, createdRole.StatusCode);
        var roleId = JsonSerializer.Deserialize<Guid>((await createdRole.ReadJsonAsync()).GetRawText());

        var email = TestEmails.Unique(prefix);
        var userId = await admin.CreateOkAsync(new { email, roles = new[] { role } });
        var client = (api ?? factory).CreateClient();
        var tokens = await client.LoginAsync(factory, email);

        return new Member(client, tokens.AccessToken, userId, role, roleId);
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await response.ReadJsonAsync();

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }

    private static async Task<JsonElement> DetailAsync(AdminUsersApi admin, Guid userId)
    {
        using var response = await admin.Client.GetWithTokenAsync($"/api/users/{userId}", admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.ReadJsonAsync();
    }

    private static async Task<string[]> RolesOfAsync(AdminUsersApi admin, Guid userId) =>
        Strings(await DetailAsync(admin, userId), "roles");

    private static async Task<JsonElement> FindRoleAsync(AdminUsersApi admin, Guid roleId)
    {
        using var response = await admin.Client.GetWithTokenAsync($"/api/roles/{roleId}", admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await response.ReadJsonAsync();
    }

    private static async Task<JsonElement> FindRoleByNameAsync(AdminUsersApi admin, string name)
    {
        using var response = await admin.Client.GetWithTokenAsync("/api/roles", admin.AccessToken);

        return (await response.ReadJsonAsync()).EnumerateArray().Single(role => role.GetProperty("name").GetString() == name);
    }

    private static string UniqueName(string prefix) =>
        prefix + "-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];

    private static string[] Strings(JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray().Select(item => item.GetString()!)];

    /// <summary>Una cuenta con un solo rol delegado, con su sesión.</summary>
    private sealed record Member(HttpClient Client, string AccessToken, Guid UserId, string Role, Guid RoleId) : IDisposable
    {
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null) =>
            Client.SendWithTokenAsync(method, path, AccessToken, body);

        public void Dispose() => Client.Dispose();
    }
}
