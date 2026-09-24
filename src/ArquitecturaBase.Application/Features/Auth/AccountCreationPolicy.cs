using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Features.Auth;

/// <summary>
/// Quién puede crear una cuenta al entrar por primera vez: cualquiera si el registro es Open y, en cualquier modo, el
/// administrador inicial (<c>Seed:AdminEmail</c>). Su cuenta se crea en su primer ingreso, así que sin esa excepción
/// una base nueva en InviteOnly, que es el modo por defecto, no deja entrar a nadie, ni a él. La regla vive solo acá:
/// la usan los pedidos de código, para decidir si se manda, y el verify y Google, para decidir si se crea la cuenta.
/// </summary>
internal sealed class AccountCreationPolicy(ISystemSettingsReader systemSettings, IInitialAdmin initialAdmin)
{
    /// <summary>
    /// Si se puede crear una cuenta nueva con <paramref name="email"/>. Null es alguien que se presenta con el número:
    /// nunca es el administrador inicial, que se reconoce por el correo, así que solo Open le crea la cuenta.
    /// </summary>
    public async Task<bool> AllowsNewAccountAsync(Email? email, CancellationToken cancellationToken) =>
        (email is not null && initialAdmin.IsInitialAdmin(email))
        || await systemSettings.GetRegistrationModeAsync(cancellationToken) is RegistrationMode.Open;
}
