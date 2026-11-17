using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// La historia de los cambios administrativos con la base real (N13): cada cambio deja su fila en la misma transacción,
/// con quién, sobre qué y lo que se agregó o se quitó; del correo, sólo el estado; el seed, sin actor. Ninguno prueba el
/// ingreso: el administrador del seed pide con la sesión de prueba, sin otro código.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class AdministrativeChangeHistoryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Giving_admin_to_an_account_records_who_gave_it_to_whom()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);
        var userId = await admin.CreateUserAsync(TestEmails.Unique("carlos"));

        using var update = await admin.UpdateUserAsync(userId, new { displayName = "Carlos", roles = new[] { SystemRoles.Admin } });

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        var changes = await ChangesOfAsync(userId);
        var given = changes[^1];
        Assert.Equal(AdministrativeChangeKind.UserRolesChanged, given.Kind);
        Assert.Equal(admin.Id, given.ActorUserId);
        Assert.Equal([SystemRoles.Admin], given.Added);
        Assert.Equal([SystemRoles.User], given.Removed);
        // El alta dejó la suya antes: la cuenta nació con User, de la mano del mismo administrador.
        Assert.Equal([SystemRoles.User], changes[0].Added);
        Assert.Equal(admin.Id, changes[0].ActorUserId);
    }

    [Fact]
    public async Task Deleting_a_role_records_the_permissions_it_had()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);
        var name = "historia-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
        using var created = await admin.SendAsync(
            HttpMethod.Post, "/api/roles", new { name, permissions = new[] { Permissions.Users.Read, Permissions.Roles.Read } });
        var roleId = JsonSerializer.Deserialize<Guid>((await created.ReadJsonAsync()).GetRawText());

        using var deleted = await admin.SendAsync(HttpMethod.Delete, $"/api/roles/{roleId}");

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
        var admin = await AdminAsync(client);
        var userId = await admin.CreateUserAsync(TestEmails.Unique("antes"));
        var newEmail = TestEmails.Unique("despues");

        using var update = await admin.UpdateUserAsync(
            userId, new { displayName = "Ana", roles = new[] { SystemRoles.User }, email = newEmail });

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
        var admin = await AdminAsync(client);
        var phone = TestPhones.Unique();
        var deleted = await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            Email.Create(TestEmails.Unique("diego")).Value, phone, phoneConfirmed: true, displayName: null, "es", Ct));
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().DeleteAsync(deleted.Id, Ct);
        });

        var restored = await admin.RestoreAsync(new { email = TestEmails.Unique("nora"), phone = AdminUsersApi.PhoneField(phone) });

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
        var admin = await AdminAsync(client);
        var email = TestEmails.Unique("carlos");
        var carlos = await admin.CreateUserAsync(new { email, displayName = "Carlos", roles = new[] { SystemRoles.Admin } });
        using (var deleted = await admin.SendAsync(HttpMethod.Delete, $"/api/users/{carlos}"))
        {
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }

        await admin.RestoreAsync(new { email, displayName = "Carlos", roles = new[] { SystemRoles.Admin } });

        // Los roles no cambiaron: no hay fila de roles, y la de la restauración dice con cuáles vuelve.
        var afterRestore = (await ChangesOfAsync(carlos))
            .SkipWhile(change => change.Kind != AdministrativeChangeKind.UserRestored).ToArray();
        var restored = Assert.Single(afterRestore, change => change.Kind == AdministrativeChangeKind.UserRestored);
        Assert.Equal([SystemRoles.Admin], restored.Added);
        Assert.Equal(admin.Id, restored.ActorUserId);
        Assert.DoesNotContain(afterRestore, change => change.Kind == AdministrativeChangeKind.UserRolesChanged);
    }

    /// <summary>
    /// La cuenta de Seed:AdminEmail nace en su primer ingreso con Admin, que le da la configuración: queda su fila, sin
    /// actor, como las del seed. La del arnés nació en el primer ingreso de la colección, de cualquier test (si ninguno
    /// entró todavía, entra ahora), y su fila queda: la base no se limpia entre tests.
    /// </summary>
    [Fact]
    public async Task The_seed_admin_email_born_on_its_first_login_has_a_trace_of_its_admin_role()
    {
        var adminId = await RequestSessions.SeedAdminIdAsync(factory);

        Assert.Contains(await ChangesOfAsync(adminId),
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

    // El administrador del seed se lee de la base (entra por código sólo si no tiene cuenta) y pide con la sesión de
    // prueba: cada código de ingreso de más cuenta para el límite de pedidos de su correo, que comparten los tests de Users.
    private async Task<Admin> AdminAsync(HttpClient client) => new(client, await RequestSessions.SeedAdminIdAsync(factory));

    /// <summary>El administrador del seed con la sesión de prueba, sin pedir otro código.</summary>
    private sealed record Admin(HttpClient Client, Guid Id)
    {
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null) =>
            Client.SendAsync(method, url, body is null ? null : JsonContent.Create(body),
                userId: Id.ToString("D", CultureInfo.InvariantCulture));

        public Task<Guid> CreateUserAsync(string email) =>
            CreateUserAsync(new { email, displayName = "Ana", roles = new[] { SystemRoles.User } });

        public async Task<Guid> CreateUserAsync(object body)
        {
            using var response = await SendAsync(HttpMethod.Post, "/api/users", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        /// <summary>El alta que restaura una cuenta borrada (N4): la respuesta lo dice. Devuelve su id.</summary>
        public async Task<Guid> RestoreAsync(object body)
        {
            using var response = await SendAsync(HttpMethod.Post, "/api/users", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var created = await response.ReadJsonAsync();
            Assert.True(created.GetProperty("restored").GetBoolean());

            return created.GetProperty("id").GetGuid();
        }

        /// <summary>La edición, con la revisión que la cuenta tiene ahora.</summary>
        public async Task<HttpResponseMessage> UpdateUserAsync(Guid userId, object body)
        {
            long revision;
            using (var current = await SendAsync(HttpMethod.Get, $"/api/users/{userId}"))
            {
                Assert.Equal(HttpStatusCode.OK, current.StatusCode);
                revision = (await current.ReadJsonAsync()).GetProperty("revision").GetInt64();
            }

            var json = JsonSerializer.SerializeToNode(body, JsonSerializerOptions.Web)!.AsObject();
            json["expectedRevision"] = revision;

            return await SendAsync(HttpMethod.Put, $"/api/users/{userId}", json);
        }
    }
}
