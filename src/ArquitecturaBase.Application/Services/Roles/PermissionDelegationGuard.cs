using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Roles;

/// <summary>
/// Nadie da lo que no tiene (N1 de la revisión del núcleo, 2026-11-14): quien pide sólo da o quita los permisos que tiene,
/// directo en un rol o con un rol en una cuenta, y sólo actúa sobre una cuenta cuyos permisos tiene todos. La usan
/// <c>RoleService</c> y <c>UserGuard</c>, adentro del límite del caso de uso.
/// <list type="bullet">
/// <item>Los permisos se leen de la base en cada llamada, nunca del caché de Redis: los roles de una cuenta, con sus
/// permisos, en una sola consulta (<see cref="IRoleReader.ListRolesForUserAsync"/>), y los roles se comparan por id, así
/// un rol renombrado entre dos lecturas no se confunde con otro.</item>
/// <item>Quien pide tiene los permisos de sus roles más los de User, que recibe toda cuenta nueva y sólo edita Admin.</item>
/// <item>La cuenta de <c>Seed:AdminEmail</c> cuenta con Admin aunque hoy no lo tenga, porque el arranque se lo da.</item>
/// <item>Como en <c>PermissionService</c>, sólo cuentan los permisos del catálogo: el claim de un módulo quitado no da ni
/// pide nada.</item>
/// </list>
/// </summary>
internal sealed class PermissionDelegationGuard(
    ICurrentUser currentUser, IRoleReader roles, IPermissionCatalog catalog, IInitialAdmin initialAdmin)
{
    /// <summary>
    /// Dar o quitar estos <paramref name="permissions"/> en un rol: quien pide tiene que tenerlos todos; si no,
    /// <see cref="RoleErrors.PermissionsNotHeld"/>.
    /// </summary>
    public async Task<Result> EnsureCanChangePermissionsAsync(
        IEnumerable<string> permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var changed = permissions.Where(catalog.Contains).ToHashSet(StringComparer.Ordinal);

        return changed.Count == 0 || changed.IsSubsetOf(await ListHeldPermissionsAsync(cancellationToken))
            ? Result.Success()
            : RoleErrors.PermissionsNotHeld;
    }

    /// <summary>
    /// Pasar una cuenta de los roles <paramref name="before"/> a <paramref name="after"/>, comparados por id: quien pide
    /// tiene que tener todos los permisos de cada rol que entra o sale; si no, <see cref="UserErrors.RolesNotHeld"/>. Un
    /// rol sin permisos lo da cualquiera que administre cuentas.
    /// </summary>
    public async Task<Result> EnsureCanChangeRolesAsync(
        IReadOnlyCollection<RoleRow> before, IReadOnlyCollection<RoleRow> after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeIds = before.Select(role => role.Id).ToHashSet();
        var afterIds = after.Select(role => role.Id).ToHashSet();
        var changed = PermissionsOf(
        [
            .. after.Where(role => !beforeIds.Contains(role.Id)),
            .. before.Where(role => !afterIds.Contains(role.Id)),
        ]);

        return changed.Count == 0 || changed.IsSubsetOf(await ListHeldPermissionsAsync(cancellationToken))
            ? Result.Success()
            : UserErrors.RolesNotHeld;
    }

    /// <summary>
    /// Actuar sobre <paramref name="user"/> (cambiarle el correo o el número, desvincularle el número, desactivarla,
    /// reactivarla o borrarla): quien pide tiene que tener todos sus permisos; si no,
    /// <see cref="UserErrors.HasMorePermissions"/>. Sobre la propia cuenta, siempre.
    /// </summary>
    public async Task<Result> EnsureCanActOnAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == currentUser.UserId)
        {
            return Result.Success();
        }

        var target = PermissionsOf(await ListRolesOfAsync(user, cancellationToken));

        return target.Count == 0 || target.IsSubsetOf(await ListHeldPermissionsAsync(cancellationToken))
            ? Result.Success()
            : UserErrors.HasMorePermissions;
    }

    /// <summary>
    /// Los roles de <paramref name="user"/>, por id y con sus permisos, en una sola consulta; la cuenta de
    /// <c>Seed:AdminEmail</c>, con Admin aunque hoy no lo tenga.
    /// </summary>
    public async Task<IReadOnlyCollection<RoleRow>> ListRolesOfAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return await WithInitialAdminAsync(
            await roles.ListRolesForUserAsync(user.Id, cancellationToken), user.Email, cancellationToken);
    }

    /// <summary>
    /// Los roles con estos <paramref name="names"/> exactos, leídos en una sola consulta (el que no existe no está), para
    /// una cuenta con <paramref name="email"/>: con el de <c>Seed:AdminEmail</c>, Admin también.
    /// </summary>
    public async Task<IReadOnlyCollection<RoleRow>> ListRolesNamedAsync(
        IReadOnlyCollection<string> names, string? email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);

        IReadOnlyCollection<RoleRow> named =
        [
            .. (await roles.ListAllRolesAsync(cancellationToken))
                .Where(role => names.Contains(role.Name, StringComparer.Ordinal)),
        ];

        return await WithInitialAdminAsync(named, email, cancellationToken);
    }

    /// <summary>Si quien pide tiene el rol Admin, leído de la base.</summary>
    public async Task<bool> IsAdminAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } actorId
        && (await roles.ListRolesForUserAsync(actorId, cancellationToken))
            .Any(role => string.Equals(role.Name, SystemRoles.Admin, StringComparison.Ordinal));

    // Los de sus roles más los de User: User lo recibe toda cuenta nueva, también las que da de alta quien administra
    // cuentas, y sólo lo edita Admin, así que lo que tiene lo tiene cualquiera.
    private async Task<HashSet<string>> ListHeldPermissionsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } actorId)
        {
            return [];
        }

        var own = await roles.ListRolesForUserAsync(actorId, cancellationToken);
        var user = await roles.FindByNameAsync(SystemRoles.User, cancellationToken);

        return PermissionsOf(user is null ? own : [.. own, user]);
    }

    // El arranque le da Admin a la cuenta de Seed:AdminEmail: tener ese correo cuenta como tener Admin. Admin no se
    // renombra, así que buscarlo por nombre es seguro.
    private async Task<IReadOnlyCollection<RoleRow>> WithInitialAdminAsync(
        IReadOnlyCollection<RoleRow> held, string? email, CancellationToken cancellationToken)
    {
        if (email is null
            || Email.Create(email) is not { IsSuccess: true } parsed
            || !initialAdmin.IsInitialAdmin(parsed.Value)
            || held.Any(role => string.Equals(role.Name, SystemRoles.Admin, StringComparison.Ordinal)))
        {
            return held;
        }

        return await roles.FindByNameAsync(SystemRoles.Admin, cancellationToken) is { } admin ? [.. held, admin] : held;
    }

    private HashSet<string> PermissionsOf(IEnumerable<RoleRow> held) =>
        held.SelectMany(role => role.Permissions).Where(catalog.Contains).ToHashSet(StringComparer.Ordinal);
}
