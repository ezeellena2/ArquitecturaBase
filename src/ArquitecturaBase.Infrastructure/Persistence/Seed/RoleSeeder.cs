using System.Security.Claims;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

/// <summary>
/// Roles del sistema con sus permisos (Admin: todos; User: ninguno por ahora), cada rol con lo que incluyen sus permisos y
/// el rol del administrador. Lo que cambia deja su historia sin actor, en el límite del seed (N13): el rol creado, los
/// permisos que recibe Admin, lo que se le completa a un rol y el Admin que recibe la cuenta de Seed:AdminEmail.
/// </summary>
internal sealed class RoleSeeder(
    RoleManager<ApplicationRole> roleManager,
    UserManager<ApplicationUser> userManager,
    ApplicationDbContext dbContext,
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
        await CompleteIncludedPermissionsAsync(cancellationToken);
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

        await AddPermissionsAsync(role, missing, existed);

        if (!existed)
        {
            Record(AdministrativeChangeKind.RoleCreated, role.Id, name, missing);
        }
        else if (missing.Count > 0)
        {
            Record(AdministrativeChangeKind.RolePermissionsChanged, role.Id, name, missing);
        }
    }

    // Lo que incluyen los permisos de cada rol y le falta, también a User y a los que creó la gente (2026-11-19): un rol
    // guardado antes de que un permiso incluyera a otro («Administrar usuarios» sin «Ver usuarios») queda con lo que
    // permite. Si no, su primera edición lo escribía en nombre de quien editaba y avanzaba la revisión aunque sólo cambiara
    // la descripción. Sólo del catálogo: el claim de un módulo quitado no incluye nada y se queda. Los roles se leen antes
    // que sus permisos: si otra réplica edita uno en el medio, su ConcurrencyStamp hace fallar el seed en lugar de
    // completarlo con lo que tenía antes.
    private async Task CompleteIncludedPermissionsAsync(CancellationToken cancellationToken)
    {
        var roles = await roleManager.Roles.ToListAsync(cancellationToken);
        var stored = (await dbContext.RoleClaims
                .Where(claim => claim.ClaimType == Permissions.ClaimType)
                .Select(claim => new { claim.RoleId, claim.ClaimValue })
                .ToListAsync(cancellationToken))
            .ToLookup(claim => claim.RoleId, claim => claim.ClaimValue!);

        foreach (var role in roles)
        {
            var current = stored[role.Id].ToHashSet(StringComparer.Ordinal);
            string[] missing =
                [.. catalog.Effective(current).Where(code => !current.Contains(code)).Order(StringComparer.Ordinal)];

            if (missing.Length > 0)
            {
                await AddPermissionsAsync(role, missing, existed: true);
                Record(AdministrativeChangeKind.RolePermissionsChanged, role.Id, role.Name, missing);
            }
        }
    }

    // Como RoleRepository: si a un rol que ya existía le cambian los permisos (Admin recibe los de un módulo nuevo, o un
    // rol lo que incluyen los suyos), avanza su revisión, así un editor abierto desde antes choca (N2). AddClaimAsync
    // guarda el rol con ella.
    private async Task AddPermissionsAsync(ApplicationRole role, IReadOnlyCollection<string> permissions, bool existed)
    {
        if (existed && permissions.Count > 0)
        {
            role.AdvanceRevision();
        }

        foreach (var permission in permissions)
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
