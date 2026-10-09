using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Identity;

internal static class DataProtectionRegistration
{
    public static void AddProtectedDataProtection(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var builder = services.AddDataProtection().SetApplicationName("ArquitecturaBase").PersistKeysToDbContext<ApplicationDbContext>();
        var section = configuration.GetSection(CertificateLoader.CertificatesSection).GetSection("DataProtection");
        if (!string.IsNullOrWhiteSpace(section["Base64"]) || !string.IsNullOrWhiteSpace(section["Path"]))
        {
            var certificate = CertificateLoader.Load(configuration, "DataProtection");
            if (!certificate.HasPrivateKey) throw new InvalidOperationException("The DataProtection PFX needs its private key.");
            builder.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
            foreach (var previous in section.GetSection("Previous").GetChildren())
            {
                var previousCertificate = LoadPrevious(previous);
                builder.UnprotectKeysWithAnyCertificate(previousCertificate);
            }
            return;
        }
        if (environment.IsDevelopment() && OperatingSystem.IsWindows())
        {
            builder.ProtectKeysWithDpapi();
            return;
        }
        if (environment.IsEnvironment(OpenIddictRegistration.TestingEnvironment))
        {
            // Test databases are disposable; a certificate is never silently generated for a deployment.
            using var rsa = RSA.Create(2048);
            var now = TimeProvider.System.GetUtcNow();
            var request = new CertificateRequest("CN=ArquitecturaBase testing", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var certificate = request.CreateSelfSigned(now.AddDays(-1), now.AddYears(1));
            builder.ProtectKeysWithCertificate(certificate).UnprotectKeysWithAnyCertificate(certificate);
            return;
        }
        // Defer until key management is resolved: EF design-time commands do not need a private deployment key.
        services.AddOptions<KeyManagementOptions>().Configure<ILoggerFactory>((_, _) =>
            throw new InvalidOperationException("Missing Authentication:Certificates:DataProtection:{Base64|Path}: provide an external PFX and Password. Plaintext DataProtection keys are not allowed."));
    }

    private static X509Certificate2 LoadPrevious(IConfigurationSection section)
    {
        var password = section["Password"];
        return !string.IsNullOrWhiteSpace(section["Base64"])
            ? X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(section["Base64"]!), password)
            : X509CertificateLoader.LoadPkcs12FromFile(section["Path"] ?? throw new InvalidOperationException("A previous DataProtection certificate needs Base64 or Path."), password);
    }
}
