using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Identity;

[Collection(ApiTestGroup.Name)]
public sealed class SeedTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Seeding_again_leaves_the_same_roles_and_permissions()
    {
        await factory.Services.SeedDatabaseAsync(Ct);

        var (roles, adminPermissions) = await factory.ExecuteDbContextAsync(async db =>
        {
            var roleNames = await db.Roles
                .Where(role => role.Name == SystemRoles.Admin || role.Name == SystemRoles.User)
                .Select(role => role.Name!)
                .ToListAsync(Ct);
            var permissions = await db.RoleClaims
                .Where(claim => claim.ClaimType == Permissions.ClaimType && db.Roles.Any(role => role.Id == claim.RoleId && role.Name == SystemRoles.Admin))
                .Select(claim => claim.ClaimValue!)
                .ToListAsync(Ct);
            return (roleNames, permissions);
        });

        Assert.Equal([SystemRoles.Admin, SystemRoles.User], roles.Order(StringComparer.Ordinal));
        Assert.Equal(factory.Services.GetRequiredService<IPermissionCatalog>().All.Order(StringComparer.Ordinal), adminPermissions.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// El seed que le devuelve a Admin un permiso que le faltaba (un módulo nuevo) o a la cuenta de Seed:AdminEmail el rol
    /// Admin cambia lo que muestra la pantalla: avanza la revisión, así un editor abierto desde antes choca (N2). Sembrar
    /// otra vez sin nada que agregar no la avanza.
    /// </summary>
    [Fact]
    public async Task Seeding_back_a_missing_admin_permission_or_role_advances_the_revision()
    {
        var permission = factory.Services.GetRequiredService<IPermissionCatalog>().All.Order(StringComparer.Ordinal).First();
        var adminId = await RequestSessions.SeedAdminIdAsync(factory);
        var before = await RevisionsAsync(adminId);
        await factory.ExecuteDbContextAsync(async db =>
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "AspNetRoleClaims"
                WHERE "RoleId" = {before.RoleId} AND "ClaimType" = {Permissions.ClaimType} AND "ClaimValue" = {permission}
                """, Ct);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM \"AspNetUserRoles\" WHERE \"UserId\" = {adminId} AND \"RoleId\" = {before.RoleId}", Ct);
        });

        await factory.Services.SeedDatabaseAsync(Ct);
        var seeded = await RevisionsAsync(adminId);
        await factory.Services.SeedDatabaseAsync(Ct);

        Assert.Equal(before.Role + 1, seeded.Role);
        Assert.Equal(before.User + 1, seeded.User);
        Assert.Equal(seeded, await RevisionsAsync(adminId));
    }

    /// <summary>
    /// Un rol guardado antes de que un permiso incluyera a otro (2026-11-19) queda al arrancar con lo que permite: el seed
    /// le agrega a cada rol, también a User y a los que creó la gente, lo que incluyen sus permisos y le falta, sólo del
    /// catálogo. Avanza su revisión y deja la historia sin actor, con lo agregado. Sembrar otra vez no cambia nada ni deja
    /// filas. Así, editar el rol después no escribe lo incluido en nombre de quien edita.
    /// </summary>
    [Fact]
    public async Task Seeding_completes_each_role_with_what_its_permissions_include_and_seeding_again_changes_nothing()
    {
        // Otro test pudo dejar un rol sin lo que incluyen sus permisos (lo escribe directo con el repositorio): esta
        // corrida lo completa antes de tomar la base.
        await factory.Services.SeedDatabaseAsync(Ct);
        var user = await RoleAsync(SystemRoles.User);
        var name = "incompleto-" + Guid.NewGuid().ToString("N")[..8];
        var custom = await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
            .CreateAsync(name, null, [Permissions.Users.Manage, "removedmodule.manage"], Ct));
        var userChanges = (await ChangesOfAsync(user.Id)).Select(change => change.Id).ToHashSet();
        try
        {
            // User, como lo guardaba antes un Admin: «Administrar roles» sin «Ver roles».
            await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
                .UpdateAsync(user.Id, SystemRoles.User, user.Description, [.. user.Permissions, Permissions.Roles.Manage], Ct));
            var before = (Custom: await RoleAsync(name), User: await RoleAsync(SystemRoles.User));

            await factory.Services.SeedDatabaseAsync(Ct);
            var seeded = (Custom: await RoleAsync(name), User: await RoleAsync(SystemRoles.User));
            var seedChanges = await SeedChangeCountAsync();
            await factory.Services.SeedDatabaseAsync(Ct);
            var again = (Custom: await RoleAsync(name), User: await RoleAsync(SystemRoles.User));

            // El claim de un módulo quitado no incluye nada y se queda.
            Assert.Equal(["removedmodule.manage", Permissions.Users.Manage, Permissions.Users.Read], seeded.Custom.Permissions);
            Assert.Equal(
                user.Permissions.Concat([Permissions.Roles.Manage, Permissions.Roles.Read]).Order(StringComparer.Ordinal),
                seeded.User.Permissions);
            Assert.Equal((before.Custom.Revision + 1, before.User.Revision + 1), (seeded.Custom.Revision, seeded.User.Revision));
            var completed = Assert.Single(await ChangesOfAsync(custom));
            Assert.Equal(AdministrativeChangeKind.RolePermissionsChanged, completed.Kind);
            Assert.Null(completed.ActorUserId);
            Assert.Equal(name, completed.TargetName);
            Assert.Equal([Permissions.Users.Read], completed.Added);
            Assert.Empty(completed.Removed);
            var userCompleted = Assert.Single(await ChangesOfAsync(user.Id), change => !userChanges.Contains(change.Id));
            Assert.Equal(AdministrativeChangeKind.RolePermissionsChanged, userCompleted.Kind);
            Assert.Null(userCompleted.ActorUserId);
            Assert.Equal([Permissions.Roles.Read], userCompleted.Added);
            Assert.Equal(seeded.Custom.Permissions, again.Custom.Permissions);
            Assert.Equal(seeded.User.Permissions, again.User.Permissions);
            Assert.Equal((seeded.Custom.Revision, seeded.User.Revision), (again.Custom.Revision, again.User.Revision));
            Assert.Equal(seedChanges, await SeedChangeCountAsync());
        }
        finally
        {
            await factory.InTransactionAsync(services => services.GetRequiredService<IRoleRepository>()
                .UpdateAsync(user.Id, SystemRoles.User, user.Description, user.Permissions, Ct));
        }
    }

    [Fact]
    public async Task Seeding_again_keeps_a_single_web_client()
    {
        await factory.Services.SeedDatabaseAsync(Ct);

        var clients = await factory.ExecuteDbContextAsync(db =>
            db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication<Guid>>()
                .CountAsync(application => application.ClientId == "web", Ct));

        Assert.Equal(1, clients);
    }

    // Con sus permisos en orden ordinal, como los lee la pantalla.
    private async Task<RoleRow> RoleAsync(string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IRoleReader>().FindByNameAsync(name, Ct)
            ?? throw new InvalidOperationException("The role does not exist.");
    }

    private Task<AdministrativeChange[]> ChangesOfAsync(Guid roleId) =>
        factory.ExecuteDbContextAsync(db => db.AdministrativeChanges.AsNoTracking()
            .Where(change => change.TargetId == roleId)
            .ToArrayAsync(Ct));

    private Task<int> SeedChangeCountAsync() =>
        factory.ExecuteDbContextAsync(db => db.AdministrativeChanges.CountAsync(change => change.ActorUserId == null, Ct));

    private Task<(Guid RoleId, long Role, long User)> RevisionsAsync(Guid adminId) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var role = await db.Roles.AsNoTracking().SingleAsync(role => role.Name == SystemRoles.Admin, Ct);
            var user = await db.Users.AsNoTracking().SingleAsync(user => user.Id == adminId, Ct);
            return (role.Id, role.Revision, user.Revision);
        });
}
