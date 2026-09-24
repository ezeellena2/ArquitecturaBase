using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Escrituras de cuentas sobre Identity. UserManager autoguarda cada operación; el caso de uso toma los locks y
/// confirma la transacción compartida con IUnitOfWork cuando combina estas escrituras con invitaciones o enlaces.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Serializa el alta o vínculo de una identidad externa por correo y clave del proveedor. Abre una transacción
    /// compartida con Identity, que confirma IUnitOfWork después de guardar la auditoría.
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
