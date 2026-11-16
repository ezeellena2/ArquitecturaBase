using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Roles del sistema con sus permisos (Admin: todos; User: ninguno por ahora) y el rol del administrador. Lo que cambia
/// deja su historia sin actor, en el límite del seed (N13): el rol creado, los permisos que recibe Admin y el Admin que
/// recibe la cuenta de Seed:AdminEmail.
/// </summary>
internal sealed class RoleSeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<SeedOptions> seedOptions,
    IPermissionCatalog catalog,
    IAdministrativeChangeRepository changes,
    TimeProvider timeProvider)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await EnsureRoleAsync(SystemRoles.Admin, catalog.All);
        await EnsureRoleAsync(SystemRoles.User, []);
        await EnsureAdminRoleAsync();
    }

    private async Task EnsureRoleAsync(string name, IReadOnlyCollection<string> permissions)
    {
        var role = await roleManager.FindByNameAsync(name);
        var existed = role is not null;

        if (role is null)
        {
            role = new ApplicationRole(name);
            (await roleManager.CreateAsync(role)).EnsureSucceeded("create the role");
        }

        var current = (await roleManager.GetClaimsAsync(role))
            .Where(claim => claim.Type == Permissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);
        var missing = permissions.Where(permission => !current.Contains(permission)).ToList();

        // Como RoleRepository: si a un rol que ya existía le cambian los permisos (Admin recibe los de un módulo nuevo),
        // avanza su revisión, así un editor abierto desde antes choca (N2). AddClaimAsync guarda el rol con ella.
        if (existed && missing.Count > 0)
        {
            role.AdvanceRevision();
        }

        foreach (var permission in missing)
        {
            (await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission))).EnsureSucceeded("add a permission");
        }

        if (!existed)
        {
            Record(AdministrativeChangeKind.RoleCreated, role.Id, name, missing);
        }
        else if (missing.Count > 0)
        {
            Record(AdministrativeChangeKind.RolePermissionsChanged, role.Id, name, missing);
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
            // Cambian los roles de la cuenta, como en UserRepository: avanza su revisión (N2), y AddToRoleAsync la guarda.
            admin.AdvanceRevision();
            (await userManager.AddToRoleAsync(admin, SystemRoles.Admin)).EnsureSucceeded("assign the Admin role");
            Record(AdministrativeChangeKind.UserRolesChanged, admin.Id, targetName: null, [SystemRoles.Admin]);
        }
    }

    // El seed no tiene actor: la fila queda con ActorUserId vacío.
    private void Record(AdministrativeChangeKind kind, Guid targetId, string? targetName, IEnumerable<string> added) =>
        changes.Add(AdministrativeChange.Create(
            kind, actorUserId: null, targetId, timeProvider.GetUtcNow().UtcDateTime, targetName, added));
}
