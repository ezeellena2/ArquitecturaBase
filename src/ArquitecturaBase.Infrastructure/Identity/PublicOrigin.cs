using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using Microsoft.Extensions.Configuration;

namespace ArquitecturaBase.Infrastructure.Identity;

/// <summary>
/// El origen público sale del issuer de OpenIddict (<c>Authentication:Issuer</c>), que ya es la dirección que ve el
/// navegador (sección 5.1 del spec). Se le agrega la "/" final si no la tiene: así una ruta relativa conserva la
/// subcarpeta, si la web vive en una.
/// </summary>
internal sealed class PublicOrigin(IConfiguration configuration) : IPublicOrigin
{
    public Uri? Value { get; } = Parse(configuration[OpenIddictRegistration.IssuerKey]);

    private static Uri? Parse(string? issuer)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        // Un issuer que no es una dirección absoluta ya hace fallar a OpenIddict al arrancar: acá no llega.
        var origin = new Uri(issuer, UriKind.Absolute);

        return origin.AbsolutePath.EndsWith('/') ? origin : new Uri(origin.AbsoluteUri + "/");
    }
}
