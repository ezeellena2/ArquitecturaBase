using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// La historia de los cambios administrativos con la base real (N13): cada cambio deja su fila en la misma transacción,
/// con quién, sobre qué y lo que se agregó o se quitó; del correo, sólo el estado; el seed, sin actor.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class AdministrativeChangeHistoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Giving_admin_to_an_account_records_who_gave_it_to_whom()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var adminId = await MyIdAsync(client, tokens.AccessToken);
        var userId = await CreateUserAsync(client, tokens.AccessToken, TestEmails.Unique("carlos"));

        using var update = await client.PutWithRevisionAsync(
            $"/api/users/{userId}", tokens.AccessToken, new { displayName = "Carlos", roles = new[] { SystemRoles.Admin } });

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var changes = await ChangesOfAsync(userId);
        var given = changes[^1];
        Assert.Equal(AdministrativeChangeKind.UserRolesChanged, given.Kind);
        Assert.Equal(adminId, given.ActorUserId);
        Assert.Equal([SystemRoles.Admin], given.Added);
        Assert.Equal([SystemRoles.User], given.Removed);
        // El alta dejó la suya antes: la cuenta nació con User, de la mano del mismo administrador.
        Assert.Equal([SystemRoles.User], changes[0].Added);
        Assert.Equal(adminId, changes[0].ActorUserId);
    }

    [Fact]
    public async Task Deleting_a_role_records_the_permissions_it_had()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = "historia-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        using var created = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/roles", tokens.AccessToken,
            new { name, permissions = new[] { Permissions.Users.Read, Permissions.Roles.Read } });
        var roleId = JsonSerializer.Deserialize<Guid>((await created.ReadJsonAsync()).GetRawText());

        using var deleted = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/roles/{roleId}", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var changes = await ChangesOfAsync(roleId);
        Assert.Equal(
            [AdministrativeChangeKind.RoleCreated, AdministrativeChangeKind.RoleDeleted],
            changes.Select(change => change.Kind));
        Assert.All(changes, change => Assert.Equal(name, change.TargetName));
        Assert.Equal([Permissions.Roles.Read, Permissions.Users.Read], changes[1].Removed);
    }

    [Fact]
    public async Task Changing_the_email_records_that_it_changed_without_the_email()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var userId = await CreateUserAsync(client, tokens.AccessToken, TestEmails.Unique("antes"));
        var newEmail = TestEmails.Unique("despues");

        using var update = await client.PutWithRevisionAsync(
            $"/api/users/{userId}", tokens.AccessToken,
            new { displayName = "Ana", roles = new[] { SystemRoles.User }, email = newEmail });

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var change = Assert.Single(await ChangesOfAsync(userId), row => row.Kind == AdministrativeChangeKind.UserEmailChanged);
        Assert.Equal(("Unverified", "Unverified"), (change.Before, change.After));
        Assert.Empty(change.Added);
        Assert.Empty(change.Removed);
        Assert.Null(change.TargetName);
    }

    [Fact]
    public async Task The_seed_records_its_changes_without_an_actor()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var admin = await db.AdministrativeChanges.AsNoTracking()
            .SingleAsync(change => change.Kind == AdministrativeChangeKind.RoleCreated && change.TargetName == SystemRoles.Admin, Ct);
        var mode = await db.AdministrativeChanges.AsNoTracking()
            .FirstAsync(change => change.Kind == AdministrativeChangeKind.RegistrationModeChanged, Ct);

        Assert.Null(admin.ActorUserId);
        Assert.Contains(Permissions.Users.Manage, admin.Added);
        Assert.Null(mode.ActorUserId);
        Assert.Null(mode.Before);
        Assert.NotNull(mode.After);
    }

    /// <summary>N4: el celular de la empresa pasa de Diego a Nora. La cuenta vuelve con otro correo, y la historia lo dice.</summary>
    [Fact]
    public async Task Restoring_by_the_phone_with_another_email_records_the_email_change()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var phone = TestPhones.Unique();
        var deleted = await admin.CreateVerifiedAccountAsync(TestEmails.Unique("diego"), phone);
        await admin.DeleteAccountAsync(deleted.Id);

        var restored = await admin.RestoreOkAsync(new { email = TestEmails.Unique("nora"), phone = AdminUsersApi.PhoneField(phone) });

        var changes = await ChangesOfAsync(restored);
        var kinds = changes.Select(change => change.Kind).ToArray();
        Assert.Contains(AdministrativeChangeKind.UserRestored, kinds);
        Assert.Contains(AdministrativeChangeKind.UserEmailChanged, kinds);
        Assert.Contains(AdministrativeChangeKind.UserPhoneChanged, kinds);
        var email = Assert.Single(changes, change => change.Kind == AdministrativeChangeKind.UserEmailChanged);
        Assert.Equal(("Verified", "Unverified"), (email.Before, email.After));
    }

    [Fact]
    public async Task Restoring_an_admin_with_admin_names_the_role_it_comes_back_with()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var email = TestEmails.Unique("carlos");
        var carlos = await admin.CreateOkAsync(new { email, displayName = "Carlos", roles = new[] { SystemRoles.Admin } });
        using (var deleted = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/users/{carlos}", admin.AccessToken))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        await admin.RestoreOkAsync(new { email, displayName = "Carlos", roles = new[] { SystemRoles.Admin } });

        // Los roles no cambiaron: no hay fila de roles, y la de la restauración dice con cuáles vuelve.
        var afterRestore = (await ChangesOfAsync(carlos))
            .SkipWhile(change => change.Kind != AdministrativeChangeKind.UserRestored).ToArray();
        var restored = Assert.Single(afterRestore, change => change.Kind == AdministrativeChangeKind.UserRestored);
        Assert.Equal([SystemRoles.Admin], restored.Added);
        Assert.Equal(admin.AdminId, restored.ActorUserId);
        Assert.DoesNotContain(afterRestore, change => change.Kind == AdministrativeChangeKind.UserRolesChanged);
    }

    /// <summary>
    /// La cuenta de Seed:AdminEmail nace en su primer ingreso con Admin, que le da la configuración: queda su fila, sin
    /// actor, como las del seed. La del arnés nació en el primer ingreso de la colección, de cualquier test, y su fila
    /// queda: la base no se limpia entre tests.
    /// </summary>
    [Fact]
    public async Task The_seed_admin_email_born_on_its_first_login_has_a_trace_of_its_admin_role()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);

        Assert.Contains(await ChangesOfAsync(admin.AdminId),
            change => change.Kind == AdministrativeChangeKind.UserRolesChanged
                && change.Added.Contains(SystemRoles.Admin)
                && change.ActorUserId is null);
    }

    [Fact]
    public async Task Running_the_seed_again_writes_no_history()
    {
        var before = await SeedChangeCountAsync();

        await factory.Services.SeedDatabaseAsync(Ct);

        Assert.Equal(before, await SeedChangeCountAsync());
    }

    private async Task<int> SeedChangeCountAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.AdministrativeChanges.CountAsync(change => change.ActorUserId == null, Ct);
    }

    private async Task<AdministrativeChange[]> ChangesOfAsync(Guid targetId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.AdministrativeChanges.AsNoTracking()
            .Where(change => change.TargetId == targetId)
            .OrderBy(change => change.OccurredAtUtc).ThenBy(change => change.Id)
            .ToArrayAsync(Ct);
    }

    private static async Task<Guid> MyIdAsync(HttpClient client, string accessToken)
    {
        using var me = await client.GetWithTokenAsync("/api/me", accessToken);
        return (await me.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateUserAsync(HttpClient client, string accessToken, string email)
    {
        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post, "/api/users", accessToken, new { email, displayName = "Ana", roles = new[] { SystemRoles.User } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
    }
}
