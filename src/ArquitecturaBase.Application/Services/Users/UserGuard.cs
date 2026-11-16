using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Roles;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Las reglas que impiden que un administrador rompa el sistema (sección 8 del spec de la Fase 4). Están acá, en un
/// solo lugar, porque las usan varios casos de uso —cambiar roles, desactivar y eliminar— y alcanza con que uno se
/// las olvide para dejar al dueño afuera. Domain no las puede resolver solo: hay que contar administradores
/// activos, y eso vive en Identity. También está la regla que impide que una persona se quede sin cómo entrar
/// (sección 12 del spec del ingreso con WhatsApp), y la que exige que los roles pedidos existan. Nadie da lo que no
/// tiene ni actúa sobre una cuenta con permisos que no tiene (<see cref="PermissionDelegationGuard"/>, N1), y el
/// administrador inicial de <c>Seed:AdminEmail</c> no pierde Admin ni el correo desde la pantalla (N25).
/// Los casos de uso llaman a las reglas de una cuenta recién después de comprobar que existe, y después de tomar sus
/// locks (los de los participantes del número y el de la cuenta): la regla del último administrador toma el lock global
/// de administradores, que va último en el orden de backend.md.
/// </summary>
internal sealed class UserGuard(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    IRoleReader roleReader,
    PermissionDelegationGuard delegation,
    IInitialAdmin initialAdmin)
{
    public bool IsCurrentUser(Guid userId) => currentUser.UserId == userId;

    /// <summary>
    /// Que existan todos los <paramref name="roles"/> pedidos en el alta o la edición, comparados por nombre exacto: si
    /// falta uno, <see cref="RoleErrors.NotFound"/>.
    /// </summary>
    public async Task<Result> EnsureRolesExistAsync(IReadOnlyCollection<string> roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var knownRoles = await roleReader.ListRoleNamesAsync(cancellationToken);

        return roles.Any(role => !knownRoles.Contains(role, StringComparer.Ordinal))
            ? RoleErrors.NotFound
            : Result.Success();
    }

    /// <summary>
    /// Los <paramref name="roles"/> del alta: quien pide tiene que tener todos sus permisos. Dar de alta el correo de
    /// <c>Seed:AdminEmail</c> cuenta como dar Admin, porque el arranque se lo da a esa cuenta.
    /// </summary>
    public async Task<Result> EnsureRolesCanBeGivenAsync(
        IReadOnlyCollection<string> roles, Email? email, CancellationToken cancellationToken) =>
        await delegation.EnsureCanChangeRolesAsync(
            [], await delegation.ListRolesNamedAsync(roles, email?.Value, cancellationToken), cancellationToken);

    /// <summary>
    /// Cambiar los roles de <paramref name="user"/> a <paramref name="roles"/>: quien pide sólo da o quita roles cuyos
    /// permisos tiene; nadie se saca a sí mismo el rol Admin; al administrador inicial no se lo saca nadie, y nadie le
    /// saca el rol al último administrador activo. Si se cumple, devuelve los roles que la cuenta tenía, por id, para
    /// volver a comprobar con <see cref="EnsureAssignedRolesAsync"/> después de asignarlos.
    /// </summary>
    public async Task<Result<IReadOnlyCollection<RoleRow>>> EnsureRolesCanChangeAsync(
        UserAccount user,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);

        var before = await delegation.ListRolesOfAsync(user, cancellationToken);
        var delegated = await delegation.EnsureCanChangeRolesAsync(
            before, await delegation.ListRolesNamedAsync(roles, user.Email, cancellationToken), cancellationToken);
        if (delegated.IsFailure)
        {
            return delegated.Error;
        }

        // Las reglas de Admin miran los roles que tiene de verdad: Admin no se renombra.
        var current = await users.ListRoleNamesForUserAsync(user.Id, cancellationToken);
        var keepsAdmin = roles.Contains(SystemRoles.Admin, StringComparer.Ordinal);

        if (!current.Contains(SystemRoles.Admin, StringComparer.Ordinal) || keepsAdmin)
        {
            return Result.Success(before);
        }

        if (user.Id == currentUser.UserId)
        {
            return UserErrors.CannotModifySelf;
        }

        if (IsInitialAdmin(user))
        {
            return UserErrors.InitialAdmin;
        }

        var remains = await EnsureAnotherAdminRemainsAsync(user.Id, current, cancellationToken);
        return remains.IsFailure ? remains.Error : Result.Success(before);
    }

    /// <summary>
    /// Después de asignar, con los roles que <paramref name="user"/> tiene de verdad, leídos por id: quien pide tiene que
    /// tener los permisos de cada uno que no estaba en <paramref name="before"/> o que salió (en el alta, vacío). Las
    /// guardas de antes comprobaron nombres, y SetRolesAsync vuelve a buscar cada rol por nombre: un rol renombrado en el
    /// medio puede ser otro. Y en el alta la cuenta puede ser una restaurada, con el correo de <c>Seed:AdminEmail</c>
    /// aunque el pedido no lo traiga: cuenta como dar Admin. Va adentro del límite: un rechazo deshace la asignación y
    /// la restauración.
    /// </summary>
    public async Task<Result> EnsureAssignedRolesAsync(
        UserAccount user, IReadOnlyCollection<RoleRow> before, CancellationToken cancellationToken) =>
        await delegation.EnsureCanChangeRolesAsync(
            before, await delegation.ListRolesOfAsync(user, cancellationToken), cancellationToken);

    /// <summary>
    /// Cambiarle a <paramref name="user"/> el correo por <paramref name="newEmail"/> o el número por
    /// <paramref name="newPhone"/> (null si no cambia): al administrador inicial no se le cambia el correo, quien pide
    /// tiene que tener todos los permisos de la cuenta, y poner el correo de <c>Seed:AdminEmail</c> cuenta como dar Admin.
    /// </summary>
    public async Task<Result> EnsureContactCanChangeAsync(
        UserAccount user, Email? newEmail, PhoneNumber? newPhone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (newEmail is null && newPhone is null)
        {
            return Result.Success();
        }

        if (newEmail is not null && IsInitialAdmin(user))
        {
            return UserErrors.InitialAdmin;
        }

        var allowed = await delegation.EnsureCanActOnAsync(user, cancellationToken);
        if (allowed.IsFailure || newEmail is null || !initialAdmin.IsInitialAdmin(newEmail))
        {
            return allowed;
        }

        return await delegation.EnsureCanChangeRolesAsync(
            [], await delegation.ListRolesNamedAsync([SystemRoles.Admin], email: null, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Desactivar o eliminar <paramref name="user"/>: nunca la propia cuenta, nunca una con permisos que quien pide no
    /// tiene, y nunca al último administrador activo.
    /// </summary>
    public async Task<Result> EnsureCanBeRemovedAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id == currentUser.UserId)
        {
            return UserErrors.CannotModifySelf;
        }

        var allowed = await delegation.EnsureCanActOnAsync(user, cancellationToken);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var roles = await users.ListRoleNamesForUserAsync(user.Id, cancellationToken);

        return await EnsureAnotherAdminRemainsAsync(user.Id, roles, cancellationToken);
    }

    /// <summary>
    /// Reactivar <paramref name="user"/>: quien pide tiene que tener todos sus permisos, como para desactivarla. Un
    /// administrador desactivado a propósito no vuelve de la mano de quien sólo administra cuentas.
    /// </summary>
    public Task<Result> EnsureCanBeActivatedAsync(UserAccount user, CancellationToken cancellationToken) =>
        delegation.EnsureCanActOnAsync(user, cancellationToken);

    /// <summary>
    /// Si <paramref name="user"/> puede entrar sin su número de WhatsApp: nadie desvincula su único medio de ingreso
    /// (sección 12 del spec del ingreso con WhatsApp). Otro medio es un correo verificado o un Google vinculado. Un
    /// correo sin verificar no cuenta: lo cargó un administrador y nadie probó todavía que la persona lo lea.
    /// </summary>
    public async Task<bool> HasOtherLoginMethodAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return (user.Email is not null && user.EmailConfirmed)
            || await users.ExistsExternalLoginAsync(user.Id, ExternalLoginProviders.Google, cancellationToken);
    }

    /// <summary>
    /// Un administrador desvincula el WhatsApp de <paramref name="user"/> (sección 12 del spec del ingreso con WhatsApp).
    /// A otra persona la puede dejar sin medio de ingreso, porque es el caso del teléfono robado: la pantalla se lo
    /// advierte, salvo que tenga permisos que él no tiene. A sí mismo no: sobre su propia cuenta conserva otro medio de
    /// ingreso (<see cref="HasOtherLoginMethodAsync"/>), como en el perfil. Lo demás del perfil no vale acá: la
    /// administración no pide el código del correo para desvincular (cada medio confirma los cambios del otro sólo desde
    /// el perfil).
    /// </summary>
    public async Task<Result> EnsurePhoneCanBeUnlinkedAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (user.Id != currentUser.UserId)
        {
            return await delegation.EnsureCanActOnAsync(user, cancellationToken);
        }

        return await HasOtherLoginMethodAsync(user, cancellationToken)
            ? Result.Success()
            : UserErrors.LastLoginMethod;
    }

    // La cuenta de Seed:AdminEmail: el seed le devuelve Admin en cada arranque, así que quitárselo desde la pantalla se
    // deshace en el próximo, y cambiarle el correo hace que el seed la pierda (N25).
    private bool IsInitialAdmin(UserAccount user) =>
        user.Email is { } email
        && Email.Create(email) is { IsSuccess: true } parsed
        && initialAdmin.IsInitialAdmin(parsed.Value);

    // El último administrador activo no se va de ninguna de las tres formas: ni quitándole el rol, ni
    // desactivándolo, ni eliminándolo. Si ya estaba inactivo no cuenta: el sistema ya estaba sin él. Cuando la regla
    // aplica, el conteo va en fila con el lock global de administradores: sin él, dos administradores que se desactivan
    // entre sí a la vez cuentan dos cada uno y el sistema queda sin ninguno. En READ COMMITTED, el conteo que sigue al
    // lock ve lo que confirmó quien lo tenía. Se toma solo acá, así una edición que no toca el rol Admin de un
    // administrador activo no pone en fila a nadie.
    private async Task<Result> EnsureAnotherAdminRemainsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (!roles.Contains(SystemRoles.Admin, StringComparer.Ordinal))
        {
            return Result.Success();
        }

        var user = await users.FindByIdAsync(userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Result.Success();
        }

        await userRepository.LockAdminsAsync(cancellationToken);

        return await users.CountActiveAdminsAsync(cancellationToken) > 1
            ? Result.Success()
            : UserErrors.LastAdmin;
    }
}
