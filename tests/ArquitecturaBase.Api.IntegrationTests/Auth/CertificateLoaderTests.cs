using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using Microsoft.Extensions.Configuration;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// Fuera de Development y Testing, OpenIddict firma y cifra con PFX propios. En un contenedor el certificado llega
/// como secreto en base64, no como archivo en disco. Sin Docker: configuración y criptografía en memoria.
/// </summary>
public sealed class CertificateLoaderTests
{
    private const string Password = "test-password";

    [Fact]
    public void Certificate_is_loaded_from_a_base64_secret()
    {
        using var expected = CreateCertificate();

        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:Certificates:Signing:Base64"] = Convert.ToBase64String(expected.Export(X509ContentType.Pfx, Password)),
            ["Authentication:Certificates:Signing:Password"] = Password,
        });

        using var loaded = CertificateLoader.Load(configuration, "Signing");

        Assert.Equal(expected.Thumbprint, loaded.Thumbprint);
        Assert.True(loaded.HasPrivateKey);
    }

    [Fact]
    public void Certificate_is_loaded_from_a_pfx_file()
    {
        using var expected = CreateCertificate();
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, expected.Export(X509ContentType.Pfx, Password));

        try
        {
            var configuration = Configuration(new Dictionary<string, string?>
            {
                ["Authentication:Certificates:Encryption:Path"] = path,
                ["Authentication:Certificates:Encryption:Password"] = Password,
            });

            using var loaded = CertificateLoader.Load(configuration, "Encryption");

            Assert.Equal(expected.Thumbprint, loaded.Thumbprint);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_base64_secret_takes_precedence_over_a_file_path()
    {
        using var expected = CreateCertificate();

        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:Certificates:Signing:Base64"] = Convert.ToBase64String(expected.Export(X509ContentType.Pfx, Password)),
            ["Authentication:Certificates:Signing:Password"] = Password,
            // Si la ruta ganara, la carga fallaría: el archivo no existe.
            ["Authentication:Certificates:Signing:Path"] = Path.Combine(Path.GetTempPath(), "no-existe.pfx"),
        });

        using var loaded = CertificateLoader.Load(configuration, "Signing");

        Assert.Equal(expected.Thumbprint, loaded.Thumbprint);
    }

    [Fact]
    public void Missing_certificate_configuration_is_rejected()
    {
        var configuration = Configuration([]);

        var exception = Assert.Throws<InvalidOperationException>(() => CertificateLoader.Load(configuration, "Signing"));

        Assert.Contains("Authentication:Certificates:Signing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_base64_secret_is_rejected()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Authentication:Certificates:Signing:Base64"] = "esto no es base64 ::",
        });

        var exception = Assert.Throws<InvalidOperationException>(() => CertificateLoader.Load(configuration, "Signing"));

        Assert.Contains("base64", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // Fechas fijas: TimeProvider no aplica acá y DateTimeOffset.UtcNow está prohibido.
    private static X509Certificate2 CreateCertificate()
    {
        using var key = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=ArquitecturaBase Tests", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        return request.CreateSelfSigned(from, from.AddYears(5));
    }
}
