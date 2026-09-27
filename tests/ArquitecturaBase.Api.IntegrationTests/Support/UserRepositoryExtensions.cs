using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class UserRepositoryExtensions
{
    /// <summary>
    /// El alta con solo correo, como la hacían las fases anteriores. Casi todos los tests arman sus usuarios así y no
    /// tienen nada que decir del número: les alcanza con esta forma corta. Va adentro de factory.InTransactionAsync, como
    /// toda escritura de cuentas.
    /// </summary>
    public static Task<UserAccount> CreateAsync(
        this IUserRepository users, Email email, string? displayName, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(users);

        return users.CreateAsync(email, phone: null, phoneConfirmed: false, displayName, culture, cancellationToken);
    }
}
