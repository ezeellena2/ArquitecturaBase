using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Escrituras de cuentas sobre Identity. Todas, igual que LockExternalSignInAsync, exigen la transacción del caso de uso
/// (IUnitOfWork.ExecuteInTransactionAsync): sin ella lanzan InvalidOperationException antes de tocar nada, también cuando
/// un test prepara datos. UserManager guarda en cada operación, dentro de esa transacción y con un savepoint por guardado:
/// un Result fallido con OnSuccess o una excepción deshacen todo, incluidos esos guardados.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Serializa el alta o vínculo de una identidad externa por correo y clave del proveedor: toma external-login: y
    /// login-code: del correo en una sola llamada (por el orden ordinal, external-login: primero). Corre dentro de la
    /// transacción del caso de uso, que abre ExternalLoginService con ExecuteInTransactionAsync y OnAnyResult: la
    /// auditoría se confirma aunque el ingreso falle. Exige esa transacción; sin ella lanza InvalidOperationException.
    /// </summary>
    Task LockExternalSignInAsync(
        Email email, string provider, string providerKey, CancellationToken cancellationToken);

    Task<UserAccount> CreateAsync(
        Email? email,
        PhoneNumber? phone,
        bool phoneConfirmed,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    Task<UserAccount> CreateUnverifiedAsync(
        Email? email,
        PhoneNumber? phone,
        string? displayName,
        string culture,
        CancellationToken cancellationToken);

    Task AddExternalLoginAsync(Guid userId, ExternalLogin login, CancellationToken cancellationToken);

    Task RestoreAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    Task SetEmailAsync(Guid userId, Email email, bool confirmed, CancellationToken cancellationToken);

    Task SetPhoneAsync(Guid userId, PhoneNumber phone, bool confirmed, CancellationToken cancellationToken);

    Task RemovePhoneAsync(Guid userId, CancellationToken cancellationToken);

    Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);

    Task SetDisplayNameAsync(Guid userId, string? displayName, CancellationToken cancellationToken);

    Task SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken);

    Task DeleteAsync(Guid userId, CancellationToken cancellationToken);

    Task UpdateProfileAsync(
        Guid userId, string? displayName, string culture, string timeZoneId, CancellationToken cancellationToken);
}
