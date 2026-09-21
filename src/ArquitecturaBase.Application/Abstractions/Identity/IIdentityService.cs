using ArquitecturaBase.Application.Common.Pagination;
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

    /// <summary>
    /// Crea el usuario con el email confirmado, porque ambos ingresos lo verifican. Le asigna el rol Admin si es el
    /// email configurado en Seed:AdminEmail, y User si no. Si Identity lo rechaza lanza una excepción: el email ya
    /// está validado y el caso de uso buscó antes al usuario, así que un rechazo es un error de programación.
    /// </summary>
    Task<UserAccount> CreateAsync(Email email, string? displayName, string culture, CancellationToken cancellationToken);

    Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>El usuario con ese email que está borrado lógicamente, o null. Lo usa el alta para restaurarlo.</summary>
    Task<UserAccount?> FindDeletedByEmailAsync(Email email, CancellationToken cancellationToken);

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
    /// Corta el acceso que ya se entregó: revoca las autorizaciones y los tokens de OpenIddict de esa persona y le
    /// renueva el security stamp, con lo que su cookie de Identity deja de valer.
    /// </summary>
    Task RevokeSessionsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Borrado lógico: la fila queda y el filtro global la esconde, así el historial sigue existiendo.</summary>
    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> IsLockedOutAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Suma una verificación fallida; al llegar al máximo, Identity bloquea la cuenta un tiempo.</summary>
    Task RegisterFailedAttemptAsync(Guid userId, CancellationToken cancellationToken);

    Task ResetFailedAttemptsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Inicia la sesión del servidor: la cookie persistente de Identity.</summary>
    Task SignInAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Lee el resultado del proveedor externo; null si no hay un ingreso externo en curso.</summary>
    Task<ExternalLogin?> GetExternalLoginAsync(CancellationToken cancellationToken);

    Task SignOutExternalAsync(CancellationToken cancellationToken);

    Task<PagedResult<UserListItem>> ListUsersAsync(PagedRequest request, CancellationToken cancellationToken);

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
}
