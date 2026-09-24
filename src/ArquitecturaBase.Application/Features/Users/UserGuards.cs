using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.Features.Users;

/// <summary>
/// Las reglas que impiden que un administrador rompa el sistema (sección 8 del spec de la Fase 4). Están acá, en un
/// solo lugar, porque las usan varios casos de uso —cambiar roles, desactivar y eliminar— y alcanza con que uno se
/// las olvide para dejar al dueño afuera. Domain no las puede resolver solo: hay que contar administradores
/// activos, y eso vive en Identity. También está la regla que impide que una persona se quede sin cómo entrar
/// (sección 12 del spec del ingreso con WhatsApp).
/// Los casos de uso llaman a estos métodos recién después de comprobar que el usuario existe.
/// </summary>
internal sealed class UserGuards(ICurrentUser currentUser, IIdentityService identityService)
{
    /// <summary>
    /// Cambiar los roles de <paramref name="userId"/> a <paramref name="roles"/>: nadie se saca a sí mismo el rol
    /// Admin y nadie le saca el rol al último administrador activo. Lo demás se puede cambiar libremente.
    /// </summary>
    public async Task<Result> EnsureRolesCanChangeAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var current = await identityService.GetRolesAsync(userId, cancellationToken);
        var keepsAdmin = roles.Contains(SystemRoles.Admin, StringComparer.Ordinal);

        if (!current.Contains(SystemRoles.Admin, StringComparer.Ordinal) || keepsAdmin)
        {
            return Result.Success();
        }

        if (userId == currentUser.UserId)
        {
            return UserErrors.CannotModifySelf;
        }

        return await EnsureAnotherAdminRemainsAsync(userId, current, cancellationToken);
    }

    /// <summary>
    /// Desactivar o eliminar <paramref name="userId"/>: nunca la propia cuenta, y nunca al último administrador
    /// activo.
    /// </summary>
    public async Task<Result> EnsureCanBeRemovedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == currentUser.UserId)
        {
            return UserErrors.CannotModifySelf;
        }

        var roles = await identityService.GetRolesAsync(userId, cancellationToken);

        return await EnsureAnotherAdminRemainsAsync(userId, roles, cancellationToken);
    }

    /// <summary>
    /// Si <paramref name="user"/> puede entrar sin su número de WhatsApp: nadie desvincula su único medio de ingreso
    /// (sección 12 del spec del ingreso con WhatsApp). Otro medio es un correo verificado o un Google vinculado. Un
    /// correo sin verificar no cuenta: lo cargó un administrador y nadie probó todavía que la persona lo lea.
    /// </summary>
    public async Task<bool> HasOtherLoginMethodAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return (user.Email is not null && user.EmailConfirmed)
            || await identityService.HasExternalLoginAsync(user.Id, ExternalLoginProviders.Google, cancellationToken);
    }

    /// <summary>
    /// Un administrador desvincula el WhatsApp de <paramref name="user"/> (sección 12 del spec del ingreso con WhatsApp).
    /// A otra persona la puede dejar sin medio de ingreso, porque es el caso del teléfono robado: la pantalla se lo
    /// advierte. A sí mismo no: sobre su propia cuenta vale la misma regla que en el perfil
    /// (<see cref="HasOtherLoginMethodAsync"/>).
    /// </summary>
    public async Task<Result> EnsurePhoneCanBeUnlinkedAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.Id != currentUser.UserId || await HasOtherLoginMethodAsync(user, cancellationToken)
            ? Result.Success()
            : UserErrors.LastLoginMethod;
    }

    // El último administrador activo no se va de ninguna de las tres formas: ni quitándole el rol, ni
    // desactivándolo, ni eliminándolo. Si ya estaba inactivo no cuenta: el sistema ya estaba sin él.
    private async Task<Result> EnsureAnotherAdminRemainsAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        CancellationToken cancellationToken)
    {
        if (!roles.Contains(SystemRoles.Admin, StringComparer.Ordinal))
        {
            return Result.Success();
        }

        var user = await identityService.FindByIdAsync(userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return Result.Success();
        }

        return await identityService.CountActiveAdminsAsync(cancellationToken) > 1
            ? Result.Success()
            : UserErrors.LastAdmin;
    }
}
