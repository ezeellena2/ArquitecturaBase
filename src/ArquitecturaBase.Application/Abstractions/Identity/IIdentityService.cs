using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Features.Roles.GetRoles;
using ArquitecturaBase.Application.Features.Users.GetUser;
using ArquitecturaBase.Application.Features.Users.GetUsers;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Abstractions.Identity;

/// <summary>
/// Acceso a usuarios, roles y sesión. Lo implementa Infrastructure sobre ASP.NET Core Identity:
/// Domain y Application no dependen del framework.
/// </summary>
public interface IIdentityService
{
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>La cuenta no borrada con ese número exacto, o null.</summary>
    Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>
    /// Crea el usuario con un correo, un número o los dos; sin ninguno lanza una <see cref="ArgumentException"/>,
    /// porque que haya al menos uno lo valida Application antes. El UserName es el Id de la cuenta, así cambiar el
    /// correo o el número no cambia nada más. El correo queda confirmado, porque ambos ingresos lo verifican; el
    /// número, según <paramref name="phoneConfirmed"/>: uno que carga un administrador queda sin verificar hasta que
    /// la persona entra con él. Le asigna el rol Admin si el correo es el configurado en Seed:AdminEmail, y User si
    /// no. Si Identity o la base lo rechazan lanza una excepción: el caso de uso buscó antes al usuario por su correo
    /// y su número, así que un rechazo es un error de programación.
    /// </summary>
    Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    /// <summary>
    /// El alta de un administrador (sección 12 del spec del ingreso con WhatsApp): como <see cref="CreateAsync"/>, pero el
    /// correo y el número quedan sin verificar hasta que la persona entra con ellos, porque nadie probó todavía que sean
    /// suyos. El ingreso con el código, Google y el bot los verifican. El alta toma el lock del destino, pero el bot y
    /// Google crean cuentas sin él: si otra cuenta se quedó con el correo o el número entre la búsqueda del alta y este
    /// guardado, lanza <see cref="UniqueConstraintViolationException"/> y no queda nada de la cuenta, ni en la base ni
    /// para guardar después. Cualquier otro rechazo sigue siendo un error de programación, como en
    /// <see cref="CreateAsync"/>.
    /// </summary>
    Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken);

    /// <summary>Si la cuenta tiene vinculado ese proveedor externo (ver <see cref="ExternalLoginProviders"/>).</summary>
    Task<bool> HasExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken);

    /// <summary>
    /// Le pone el número a la cuenta, verificado o no. Solo escribe el dato: no renueva el security stamp, que le
    /// cortaría la cookie a quien vincula su propio número desde el perfil. Si hay que cerrar las sesiones, lo decide
    /// quien llama con <see cref="RevokeSessionsAsync"/>. El número tiene índice único: quien llama se fija antes con
    /// <see cref="FindByPhoneAsync"/> e <see cref="IsDeletedPhoneAsync"/>. Si igual choca, porque otra cuenta lo guardó
    /// entre esa búsqueda y este guardado, lanza <see cref="UniqueConstraintViolationException"/> y la cuenta queda como
    /// estaba: el resto de la unidad de trabajo se puede guardar igual.
    /// </summary>
    Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken);

    /// <summary>
    /// Le saca el número a la cuenta y lo deja sin verificar. Como <see cref="SetPhoneAsync"/>, solo escribe el dato:
    /// cuando lo desvincula un administrador, el caso de uso cierra las sesiones con <see cref="RevokeSessionsAsync"/>.
    /// </summary>
    Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Le pone el correo a la cuenta, verificado o no, y recalcula el normalizado que usa la búsqueda por correo. Como
    /// <see cref="SetPhoneAsync"/>, solo escribe el dato y no renueva el security stamp. El correo tiene índice único:
    /// quien llama se fija antes con <see cref="FindByEmailAsync"/> e <see cref="IsDeletedEmailAsync"/>, y un choque
    /// posterior se trata igual que con el número.
    /// </summary>
    Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>El usuario con ese email que está borrado lógicamente, o null. Lo usa el alta para restaurarlo.</summary>
    Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// El usuario con ese número que está borrado lógicamente, o null. Lo usa el bot de WhatsApp, que le contesta a una
    /// cuenta borrada como a una deshabilitada y en su idioma: por eso necesita la cuenta y no le alcanza con
    /// <see cref="IsDeletedPhoneAsync"/>.
    /// </summary>
    Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>Deshace el borrado lógico, deja la cuenta activa y le pone el nombre del alta.</summary>
    Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    /// <summary>Deja al usuario exactamente con esos roles: agrega los que faltan y saca los que sobran.</summary>
    Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

    /// <summary>Los nombres de todos los roles. El alta y la edición validan contra esta lista.</summary>
    Task<IReadOnlyCollection<string>> ListRoleNamesAsync(CancellationToken cancellationToken);

    /// <summary>El detalle del usuario con sus roles, o null si no existe.</summary>
    Task<UserDetail?> FindDetailAsync(Guid userId, CancellationToken cancellationToken);

    Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>
    /// Corta el acceso que ya se entregó: revoca las autorizaciones y los tokens de OpenIddict de esa persona, le
    /// renueva el security stamp, con lo que su cookie de Identity deja de valer, e invalida los enlaces de ingreso que
    /// el bot le haya mandado y todavía no se usaron.
    /// </summary>
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Borrado lógico: la fila queda y el filtro global la esconde, así el historial sigue existiendo.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Todos los roles, ordenados por nombre, con sus permisos y sus usuarios.</summary>
    Task<IReadOnlyCollection<RoleListItem>> ListRolesAsync(CancellationToken cancellationToken);

    Task<RoleListItem?> FindRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>Si ya hay un rol con ese nombre, sin contar a <paramref name="excludedRoleId"/>.</summary>
    Task<bool> RoleNameExistsAsync(string name, Guid? excludedRoleId, CancellationToken cancellationToken);

    Task<Guid> CreateRoleAsync(
        string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task UpdateRoleAsync(
        Guid roleId, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken);

    Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken);

    /// <summary>El perfil que edita el propio usuario desde PUT /api/me.</summary>
    Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken);

    Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Suma una verificación fallida; al llegar al máximo, Identity bloquea la cuenta un tiempo.</summary>
    Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken);

    Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Inicia la sesión del servidor: la cookie persistente de Identity.</summary>
    Task SignInAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Lee el resultado del proveedor externo; null si no hay un ingreso externo en curso.</summary>
    Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken);

    Task SignOutExternalAsync(CancellationToken cancellationToken);

    Task<PagedResult<UserListItem>> ListUsersAsync(UserListRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Cuántos usuarios traería cada opción de filtro, con los mismos filtros que <see cref="ListUsersAsync"/>.
    /// </summary>
    Task<UserFilterCounts> GetUserFilterCountsAsync(UserListRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Cuántos usuarios activos tienen el rol Admin. Lo usa <c>UserGuards</c> para no dejar al sistema sin
    /// administradores.
    /// </summary>
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// True si ese email pertenece a una cuenta borrada lógicamente. El filtro global las oculta de todas las demás
    /// búsquedas, así que sin esto un ingreso intentaría crear una cuenta nueva y chocaría con el índice único del
    /// email.
    /// </summary>
    Task<bool> IsDeletedEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// True si ese número pertenece a una cuenta borrada lógicamente. Como con el correo, la cuenta borrada conserva
    /// su número y el índice único lo sigue reservando.
    /// </summary>
    Task<bool> IsDeletedPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);
}
