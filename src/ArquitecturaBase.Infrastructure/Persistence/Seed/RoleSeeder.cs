using System.Security.Claims;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>Roles del sistema con sus permisos (Admin: todos; User: ninguno por ahora) y el rol del administrador.</summary>
internal sealed class RoleSeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<SeedOptions> seedOptions)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureRoleAsync(SystemRoles.Admin, Permissions.All);
        await EnsureRoleAsync(SystemRoles.User, []);
        await EnsureAdminRoleAsync();
    }

    private async Task EnsureRoleAsync(string name, IReadOnlyCollection<string> permissions)
    {
        var role = await roleManager.FindByNameAsync(name);

        if (role is null)
        {
            role = new ApplicationRole(name);
            (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
        }

        var current = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Permissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var permission in permissions.Where(permission => !current.Contains(permission)))
        {
            (await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission))).EnsureSucceeded("add a permission");
        }
    }

    // Si la cuenta ya existía cuando se configuró Seed:AdminEmail, recibe el rol acá.
    private async Task EnsureAdminRoleAsync()
    {
        var adminEmail = Email.Create(seedOptions.Value.AdminEmail);

        if (adminEmail.IsFailure)
        {
            return;
        }

        var admin = await userManager.FindByEmailAsync(adminEmail.Value.Value);

        if (admin is not null && !await userManager.IsInRoleAsync(admin, SystemRoles.Admin))
        {
            (await userManager.AddToRoleAsync(admin, SystemRoles.Admin)).EnsureSucceeded("assign the Admin role");
        }
    }
}
