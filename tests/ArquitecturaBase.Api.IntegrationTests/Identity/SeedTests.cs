using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;
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

    [Fact]
    public async Task Seeding_again_keeps_a_single_web_client()
    {
        await factory.Services.SeedDatabaseAsync(Ct);

        var clients = await factory.ExecuteDbContextAsync(db =>
            db.Set<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication<Guid>>()
                .CountAsync(application => application.ClientId == "web", Ct));

        Assert.Equal(1, clients);
    }

    private Task<(Guid RoleId, long Role, long User)> RevisionsAsync(Guid adminId) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var role = await db.Roles.AsNoTracking().SingleAsync(role => role.Name == SystemRoles.Admin, Ct);
            var user = await db.Users.AsNoTracking().SingleAsync(user => user.Id == adminId, Ct);
            return (role.Id, role.Revision, user.Revision);
        });
}
