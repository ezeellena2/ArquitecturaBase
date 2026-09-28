using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Lecturas de cuentas: proyecciones sin seguimiento, existencia y conteos. Respetan el filtro global de borrados, salvo
/// las que dicen Deleted. Nunca devuelven la entidad de Identity y no exigen transacción. Van siempre a la base: una
/// relectura después de tomar un lock ve lo que otro ya confirmó. Para escribir, <see cref="IUserRepository"/>; para el
/// bloqueo y la sesión, <see cref="Integrations.Identity.ISignInService"/>.
/// </summary>
public interface IUserReader
{
    Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserAccount?> FindByEmailAsync(Email email, CancellationToken cancellationToken);

    Task<UserAccount?> FindByExternalLoginAsync(string provider, string providerKey, CancellationToken cancellationToken);

    /// <summary>La cuenta no borrada con ese número exacto, o null.</summary>
    Task<UserAccount?> FindByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>
    /// Si ese correo es de una cuenta borrada lógicamente. El filtro global las oculta de todas las demás búsquedas, así
    /// que sin esto un ingreso intentaría crear una cuenta nueva y chocaría con el índice único del correo.
    /// </summary>
    Task<bool> ExistsDeletedByEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// Si ese número es de una cuenta borrada lógicamente. Como con el correo, la cuenta borrada conserva su número y el
    /// índice único lo sigue reservando.
    /// </summary>
    Task<bool> ExistsDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>La cuenta borrada lógicamente con ese correo, o null. La usa el alta para restaurarla.</summary>
    Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>
    /// La cuenta borrada lógicamente con ese número, o null. La usa el bot de WhatsApp, que le contesta a una cuenta
    /// borrada como a una deshabilitada y en su idioma: por eso necesita la cuenta y no le alcanza con
    /// <see cref="ExistsDeletedByPhoneAsync"/>.
    /// </summary>
    Task<UserAccount?> FindDeletedByPhoneAsync(PhoneNumber phone, CancellationToken cancellationToken);

    /// <summary>Si la cuenta tiene vinculado ese proveedor externo (ver <see cref="ExternalLoginProviders"/>).</summary>
    Task<bool> ExistsExternalLoginAsync(Guid userId, string provider, CancellationToken cancellationToken);

    /// <summary>
    /// Los roles asignados a una cuenta, sin imponer un orden de presentación: <see cref="FindDetailAsync"/> y
    /// ConnectService los ordenan.
    /// </summary>
    Task<IReadOnlyCollection<string>> ListRoleNamesForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>El detalle de la cuenta, con los roles en orden ordinal, o null si no existe o está borrada.</summary>
    Task<UserDetailRow?> FindDetailAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cuántas cuentas activas tienen el rol Admin. Lo usa <c>UserGuards</c> para no dejar al sistema sin
    /// administradores.
    /// </summary>
    Task<int> CountActiveAdminsAsync(CancellationToken cancellationToken);

    Task<PagedResult<UserListRow>> ListUsersAsync(ListUsersRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Cuántas cuentas traería cada opción de filtro, con los mismos filtros que <see cref="ListUsersAsync"/> salvo el
    /// propio.
    /// </summary>
    Task<UserFilterCounts> CountByFilterOptionAsync(ListUsersRequest request, CancellationToken cancellationToken);
}
