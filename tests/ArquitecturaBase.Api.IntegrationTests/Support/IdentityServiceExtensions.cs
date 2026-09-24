using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class IdentityServiceExtensions
{
    /// <summary>
    /// El alta con solo correo, como la hacían las fases anteriores. Casi todos los tests arman sus usuarios así y
    /// no tienen nada que decir del número: les alcanza con esta forma corta.
    /// </summary>
    public static Task<UserAccount> CreateAsync(
        this IIdentityService identity, Email email, string? displayName, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);

        return identity.CreateAsync(email, phone: null, phoneConfirmed: false, displayName, culture, cancellationToken);
    }
}
