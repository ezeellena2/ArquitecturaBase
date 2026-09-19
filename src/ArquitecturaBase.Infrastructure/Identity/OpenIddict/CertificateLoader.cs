using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;

namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

/// <summary>
/// Carga los PFX de firma y cifrado de OpenIddict desde Authentication:Certificates:{Encryption|Signing}.
/// Base64 gana sobre Path: en un contenedor el certificado llega como secreto o variable de entorno, no como
/// archivo en disco. Los constructores de X509Certificate2 están obsoletos (SYSLIB0057).
/// </summary>
internal static class CertificateLoader
{
    public const string CertificatesSection = "Authentication:Certificates";

    public static X509Certificate2 Load(IConfiguration configuration, string purpose)
    {
        var section = configuration.GetSection(CertificatesSection).GetSection(purpose);
        var password = section["Password"];
        var base64 = section["Base64"];

        if (!string.IsNullOrWhiteSpace(base64))
        {
            return LoadFromBase64(base64, password, purpose);
        }

        var path = section["Path"];

        if (!string.IsNullOrWhiteSpace(path))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(path, password);
        }

        throw new InvalidOperationException(
            $"Missing '{CertificatesSection}:{purpose}:Base64' or '{CertificatesSection}:{purpose}:Path': " +
            "OpenIddict needs PFX certificates outside Development and Testing.");
    }

    private static X509Certificate2 LoadFromBase64(string base64, string? password, string purpose)
    {
        byte[] pfx;

        try
        {
            pfx = Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            // El valor nunca se registra: es la clave privada del servidor de tokens.
            throw new InvalidOperationException(
                $"'{CertificatesSection}:{purpose}:Base64' is not valid base64.", exception);
        }

        return X509CertificateLoader.LoadPkcs12(pfx, password);
    }
}
