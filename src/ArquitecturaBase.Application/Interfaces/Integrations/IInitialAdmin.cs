using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>
/// El administrador inicial: el correo configurado en <c>Seed:AdminEmail</c>, que recibe el rol Admin al crearse su
/// cuenta. Lo implementa Infrastructure, que es la que conoce la configuración del seed. Se reconoce solo por el
/// correo: una cuenta de solo número nunca es la suya.
/// </summary>
public interface IInitialAdmin
{
    /// <summary>Si <paramref name="email"/> es el del administrador inicial. Sin <c>Seed:AdminEmail</c>, ninguno lo es.</summary>
    bool IsInitialAdmin(Email email);
}
