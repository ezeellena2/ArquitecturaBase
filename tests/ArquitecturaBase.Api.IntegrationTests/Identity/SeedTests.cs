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
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), adminPermissions.Order(StringComparer.Ordinal));
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
}
