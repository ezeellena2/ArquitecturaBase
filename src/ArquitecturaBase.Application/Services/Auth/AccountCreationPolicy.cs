using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Application.Models.Settings;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// Quién puede crear una cuenta al entrar por primera vez: cualquiera si el registro es Open y, en cualquier modo, el
/// administrador inicial (<c>Seed:AdminEmail</c>). Su cuenta se crea en su primer ingreso, así que sin esa excepción
/// una base nueva en InviteOnly, que es el modo por defecto, no deja entrar a nadie, ni a él. La regla vive solo acá:
/// la usan los pedidos de código, para decidir si se manda, y el verify y Google, para decidir si se crea la cuenta.
/// </summary>
internal sealed class AccountCreationPolicy(ISystemSettingsReader systemSettings, IInitialAdmin initialAdmin)
{
    public async Task<SystemPresentationResponse> GetPreferencesAsync(bool fromCurrentRequest, CancellationToken cancellationToken)
    {
        var settings = await systemSettings.FindPresentationAsync(cancellationToken);
        return fromCurrentRequest ? settings with { DefaultCulture = UserCultures.FromCurrentRequest() } : settings;
    }

    public bool IsInitialAdmin(Email? email) => email is not null && initialAdmin.IsInitialAdmin(email);
    /// <summary>
    /// Si se puede crear una cuenta nueva con <paramref name="email"/>. Null es alguien que se presenta con el número:
    /// nunca es el administrador inicial, que se reconoce por el correo, así que solo Open le crea la cuenta.
    /// </summary>
    public async Task<bool> AllowsNewAccountAsync(Email? email, CancellationToken cancellationToken) =>
        IsInitialAdmin(email)
        || await systemSettings.FindRegistrationModeAsync(cancellationToken) is RegistrationMode.Open;
}
