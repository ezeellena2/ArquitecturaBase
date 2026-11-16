using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Usuarios y roles con control de revisión (N2, B2, O4): dos ediciones leídas en la misma revisión no se pisan, el perfil
/// y el administrador tampoco, lo que no se edita no avanza la revisión, y borrar un rol mientras se asigna da un error de
/// negocio, nunca un 500 ni una asignación borrada en cascada. Las carreras corren con dos transacciones reales.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class RevisionConflictTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Two_edits_of_an_account_read_at_the_same_revision_let_only_the_first_through()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);
        var userId = await admin.CreateOkAsync(new { email = TestEmails.Unique("rev"), displayName = "Ana" });
        var revision = (await admin.DetailAsync(userId)).GetProperty("revision").GetInt64();

        using var first = await admin.UpdateAsync(
            userId, new { displayName = "Ana María", roles = new[] { SystemRoles.User }, expectedRevision = revision });
        using var second = await admin.UpdateAsync(
            userId, new { displayName = "Anita", roles = new[] { SystemRoles.User }, expectedRevision = revision }, "es");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.ReadJsonAsync();
        Assert.Equal(UserErrors.RevisionConflictCode, problem.GetProperty("code").GetString());
        Assert.Equal(
            "La cuenta cambió mientras la editabas. Volvé a leerla y revisá tus cambios antes de guardar.",
            problem.GetProperty("detail").GetString());
        var stored = await admin.DetailAsync(userId);
        Assert.Equal("Ana María", stored.GetProperty("displayName").GetString());
        Assert.True(stored.GetProperty("revision").GetInt64() > revision);
    }

    [Fact]
    public async Task Two_edits_of_a_role_read_at_the_same_revision_let_only_the_first_through()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);
        using var created = await admin.SendAsync(
            HttpMethod.Post, "/api/roles", new { name = UniqueRole(), permissions = new[] { Permissions.Users.Read } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var roleId = (await created.ReadJsonAsync()).GetGuid();
        using var read = await admin.SendAsync(HttpMethod.Get, $"/api/roles/{roleId}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var role = await read.ReadJsonAsync();
        var revision = role.GetProperty("revision").GetInt64();
        var name = role.GetProperty("name").GetString();

        // Ana le saca un permiso; Bruno, con la pantalla abierta desde antes, sólo cambia la descripción.
        using var first = await admin.SendAsync(HttpMethod.Put, $"/api/roles/{roleId}",
            new { name, permissions = Array.Empty<string>(), expectedRevision = revision });
        using var second = await admin.SendAsync(HttpMethod.Put, $"/api/roles/{roleId}",
            new { name, description = "Otra", permissions = new[] { Permissions.Users.Read }, expectedRevision = revision },
            "en");

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = await second.ReadJsonAsync();
        Assert.Equal(RoleErrors.RevisionConflictCode, problem.GetProperty("code").GetString());
        Assert.Equal(
            "The role changed while you were editing it. Reload it and review your changes before saving.",
            problem.GetProperty("detail").GetString());
        using var after = await admin.SendAsync(HttpMethod.Get, $"/api/roles/{roleId}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.Empty((await after.ReadJsonAsync()).GetProperty("permissions").EnumerateArray());
    }

    [Fact]
    public async Task The_profile_does_not_overwrite_a_name_the_administrator_changed_in_between()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);
        var userId = await admin.CreateOkAsync(new { email = TestEmails.Unique("perfil"), displayName = "Beto" });
        var id = userId.ToString("D", System.Globalization.CultureInfo.InvariantCulture);
        using var me = await client.SendAsync(HttpMethod.Get, "/api/me", userId: id);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var revision = (await me.ReadJsonAsync()).GetProperty("revision").GetInt64();

        using var byAdmin = await admin.UpdateAsync(userId, new { displayName = "Roberto", roles = new[] { SystemRoles.User } });
        using var byProfile = await client.SendAsync(HttpMethod.Put, "/api/me", JsonContent.Create(new
        {
            displayName = "Betito",
            culture = "es",
            timeZoneId = "America/Argentina/Buenos_Aires",
            expectedRevision = revision,
        }), userId: id);

        Assert.Equal(HttpStatusCode.NoContent, byAdmin.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, byProfile.StatusCode);
        var problem = await byProfile.ReadJsonAsync();
        Assert.Equal(UserErrors.RevisionConflictCode, problem.GetProperty("code").GetString());
        Assert.Equal("Roberto", (await admin.DetailAsync(userId)).GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task A_failed_attempt_and_its_reset_do_not_advance_the_revision()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);
        var userId = await admin.CreateOkAsync(new { email = TestEmails.Unique("intentos"), displayName = "Carla" });
        var revision = (await admin.DetailAsync(userId)).GetProperty("revision").GetInt64();

        // Un código mal escrito y el reinicio de intentos cambian el ConcurrencyStamp de la cuenta, no lo que se edita.
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            async ct =>
            {
                var signIn = services.GetRequiredService<ISignInService>();
                await signIn.RegisterFailedAttemptAsync(userId, ct);
                await signIn.ResetFailedAttemptsAsync(userId, ct);
                return Result.Success();
            }, CommitPolicy.OnSuccess, Ct));

        Assert.Equal(revision, (await admin.DetailAsync(userId)).GetProperty("revision").GetInt64());
        using var edit = await admin.UpdateAsync(
            userId, new { displayName = "Carla B.", roles = new[] { SystemRoles.User }, expectedRevision = revision });
        Assert.Equal(HttpStatusCode.NoContent, edit.StatusCode);
    }

    [Fact]
    public async Task Activating_and_deactivating_an_account_at_once_never_fails_with_a_server_error()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);
        var userId = await admin.CreateOkAsync(new { email = TestEmails.Unique("estado"), displayName = "Dani" });

        // O4: sin el lock de la fila, el segundo guardado chocaba con el ConcurrencyStamp y respondía 500.
        for (var round = 0; round < 5; round++)
        {
            var responses = await Task.WhenAll(
                admin.SendAsync(HttpMethod.Post, $"/api/users/{userId}/deactivate"),
                admin.SendAsync(HttpMethod.Post, $"/api/users/{userId}/activate"),
                admin.SendAsync(HttpMethod.Post, $"/api/users/{userId}/deactivate"));

            Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task Deleting_a_role_while_it_is_being_assigned_waits_and_counts_the_assignment()
    {
        var adminId = await AdminIdAsync();
        var roleId = await CreateRoleAsync(adminId);
        var userId = await CreateAccountAsync();
        var options = factory.Services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        await using var observer = new ApplicationDbContext(options);

        Task<Result> deleting;

        // Otra conexión asigna el rol: lo bloquea como la asignación (FOR KEY SHARE) e inserta la fila, sin confirmar.
        await using (var holder = new ApplicationDbContext(options))
        await using (var transaction = await holder.Database.BeginTransactionAsync(Ct))
        {
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AspNetRoles\" WHERE \"Id\" = {roleId} FOR KEY SHARE", Ct);
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"AspNetUserRoles\" (\"UserId\", \"RoleId\") VALUES ({userId}, {roleId})", Ct);

            deleting = Task.Run(() => factory.ExecuteScopeAsync(services =>
            {
                services.ActAs(adminId);
                return services.GetRequiredService<IRoleService>().DeleteAsync(new DeleteRoleRequest(roleId), Ct);
            }), Ct);

            await WaitUntilItWaitsForARowLockAsync(observer, deleting);
            await transaction.CommitAsync(Ct);
        }

        var result = await deleting.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(RoleErrors.HasUsersCode, result.Error.Code);
        Assert.Contains(roleId, await RoleIdsOfAsync(userId));
    }

    [Fact]
    public async Task Assigning_a_role_while_it_is_being_deleted_waits_and_finds_it_gone()
    {
        var adminId = await AdminIdAsync();
        var roleId = await CreateRoleAsync(adminId);
        var roleName = await factory.ExecuteScopeAsync(async services =>
            (await services.GetRequiredService<IRoleReader>().FindByIdAsync(roleId, Ct))!.Name);
        var userId = await CreateAccountAsync();
        var revision = await factory.ExecuteScopeAsync(async services =>
            (await services.GetRequiredService<IUserReader>().FindByIdAsync(userId, Ct))!.Revision);
        var options = factory.Services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        await using var observer = new ApplicationDbContext(options);

        Task<Result> assigning;

        // Otra conexión borra el rol, como el borrado (FOR UPDATE y DELETE), sin confirmar.
        await using (var holder = new ApplicationDbContext(options))
        await using (var transaction = await holder.Database.BeginTransactionAsync(Ct))
        {
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AspNetRoles\" WHERE \"Id\" = {roleId} FOR UPDATE", Ct);
            await holder.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"AspNetRoles\" WHERE \"Id\" = {roleId}", Ct);

            assigning = Task.Run(() => factory.ExecuteScopeAsync(services =>
            {
                services.ActAs(adminId);
                return services.GetRequiredService<IUserAdministrationService>().UpdateUserAsync(
                    new UpdateUserRequest(userId, "Eva", [SystemRoles.User, roleName], revision), Ct);
            }), Ct);

            await WaitUntilItWaitsForARowLockAsync(observer, assigning);
            await transaction.CommitAsync(Ct);
        }

        var result = await assigning.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.Equal(RoleErrors.NotFoundCode, result.Error.Code);
        Assert.DoesNotContain(roleId, await RoleIdsOfAsync(userId));
    }

    [Fact]
    public async Task An_email_change_and_a_sign_in_that_references_the_account_do_not_deadlock()
    {
        var adminId = await AdminIdAsync();
        var userId = await CreateAccountAsync();
        var revision = await factory.ExecuteScopeAsync(async services =>
            (await services.GetRequiredService<IUserReader>().FindByIdAsync(userId, Ct))!.Revision);
        var newEmail = TestEmails.Unique("cambio");
        var options = factory.Services.GetRequiredService<DbContextOptions<ApplicationDbContext>>();
        await using var observer = new ApplicationDbContext(options);

        Task<Result> editing;

        // Otra conexión hace lo que un ingreso: inserta una fila que apunta a la cuenta (la clave foránea la toma con FOR
        // KEY SHARE hasta el commit) y después escribe la cuenta (el reinicio de intentos). El UPDATE que cambia el correo,
        // una columna con índice único, pide FOR UPDATE: si la edición tomara la fila con FOR NO KEY UPDATE, que no choca
        // con FOR KEY SHARE, se esperarían una a la otra y Postgres cortaría a una con un deadlock (40P01).
        await using (var holder = new ApplicationDbContext(options))
        await using (var transaction = await holder.Database.BeginTransactionAsync(Ct))
        {
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"AspNetUsers\" WHERE \"Id\" = {userId} FOR KEY SHARE", Ct);

            editing = Task.Run(() => factory.ExecuteScopeAsync(services =>
            {
                services.ActAs(adminId);
                return services.GetRequiredService<IUserAdministrationService>().UpdateUserAsync(
                    new UpdateUserRequest(userId, "Eva", [SystemRoles.User], revision, newEmail), Ct);
            }), Ct);

            await WaitUntilItWaitsForARowLockAsync(observer, editing);
            await holder.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE \"AspNetUsers\" SET \"AccessFailedCount\" = 0 WHERE \"Id\" = {userId}", Ct);
            await transaction.CommitAsync(Ct);
        }

        var result = await editing.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(newEmail, await factory.ExecuteScopeAsync(async services =>
            (await services.GetRequiredService<IUserReader>().FindByIdAsync(userId, Ct))!.Email));
    }

    private static string UniqueRole() => "Rev " + Guid.CreateVersion7().ToString("N")[..12];

    private async Task<Guid> CreateRoleAsync(Guid adminId)
    {
        var created = await factory.ExecuteScopeAsync(services =>
        {
            services.ActAs(adminId);
            return services.GetRequiredService<IRoleService>()
                .CreateAsync(new CreateRoleRequest(UniqueRole(), null, [Permissions.Users.Read]), Ct);
        });
        Assert.True(created.IsSuccess);

        return created.Value;
    }

    private async Task<Guid> CreateAccountAsync()
    {
        using var client = factory.CreateClient();
        var admin = await AdminAsync(client);

        return await admin.CreateOkAsync(new { email = TestEmails.Unique("asigna"), displayName = "Eva" });
    }

    // El administrador del seed se lee de la base (entra por código sólo si no tiene cuenta): los pedidos van con la
    // sesión de prueba, y cada código de ingreso de más cuenta para el límite de su correo.
    private static readonly SemaphoreSlim AdminGate = new(1, 1);
    private static Guid? _adminId;

    private async Task<Guid> AdminIdAsync()
    {
        await AdminGate.WaitAsync(Ct);
        try
        {
            return _adminId ??= await RequestSessions.SeedAdminIdAsync(factory);
        }
        finally
        {
            AdminGate.Release();
        }
    }

    private async Task<Admin> AdminAsync(HttpClient client) => new(client, await AdminIdAsync());

    /// <summary>El administrador del seed con la sesión de prueba, sin pedir otro código.</summary>
    private sealed record Admin(HttpClient Client, Guid Id)
    {
        private string UserId => Id.ToString("D", System.Globalization.CultureInfo.InvariantCulture);

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? language = null) =>
            Client.SendAsync(method, url, body is null ? null : JsonContent.Create(body), language, UserId);

        public async Task<Guid> CreateOkAsync(object body)
        {
            using var response = await SendAsync(HttpMethod.Post, "/api/users", body);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            return (await response.ReadJsonAsync()).GetProperty("id").GetGuid();
        }

        public async Task<System.Text.Json.JsonElement> DetailAsync(Guid userId)
        {
            using var response = await SendAsync(HttpMethod.Get, $"/api/users/{userId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            return await response.ReadJsonAsync();
        }

        /// <summary>La edición; sin <c>expectedRevision</c> en el cuerpo, con la que la cuenta tiene ahora.</summary>
        public async Task<HttpResponseMessage> UpdateAsync(Guid userId, object body, string? language = null)
        {
            var json = System.Text.Json.JsonSerializer.SerializeToNode(body, System.Text.Json.JsonSerializerOptions.Web)!.AsObject();
            if (!json.ContainsKey("expectedRevision"))
            {
                json["expectedRevision"] = (await DetailAsync(userId)).GetProperty("revision").GetInt64();
            }

            return await SendAsync(HttpMethod.Put, $"/api/users/{userId}", json, language);
        }
    }

    private async Task<IReadOnlyCollection<Guid>> RoleIdsOfAsync(Guid userId) =>
        await factory.ExecuteScopeAsync(async services =>
            (IReadOnlyCollection<Guid>)[.. (await services.GetRequiredService<IRoleReader>().ListRolesForUserAsync(userId, Ct))
                .Select(role => role.Id)]);

    /// <summary>
    /// Espera a que otra conexión de esta base quede esperando un lock, o a que el caso de uso termine: si termina antes,
    /// no esperó, y el test lo dice.
    /// </summary>
    private static async Task WaitUntilItWaitsForARowLockAsync(ApplicationDbContext observer, Task running)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (!running.IsCompleted
            && await observer.Database
                .SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value" FROM pg_stat_activity
                    WHERE datname = current_database() AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()
                    """)
                .SingleAsync(timeout.Token) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }

        Assert.False(running.IsCompleted, "The use case finished while another connection held the row.");
    }
}
