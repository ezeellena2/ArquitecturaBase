using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>
/// Hace de ARCA al emitir certificados: una CA autofirmada por ambiente, con el CN que espera la configuración de prueba.
/// Firma el pedido tal como llega, así el certificado lleva la clave y el sujeto del pedido.
/// </summary>
internal sealed class FakeArcaCertificateAuthority : IDisposable
{
    public const string TestingCommonName = "Computadores Test";
    public const string ProductionCommonName = "Computadores";

    private static readonly DateTimeOffset ValidFrom = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidUntil = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly X509Certificate2 testing = CreateAuthority(TestingCommonName);
    private readonly X509Certificate2 production = CreateAuthority(ProductionCommonName);

    /// <summary>Emite un certificado de dos años que vence en <paramref name="notAfterUtc"/>.</summary>
    public string Issue(string requestPem, ArcaEnvironment environment, DateTime notAfterUtc)
    {
        var request = CertificateRequest.LoadSigningRequestPem(
            requestPem, HashAlgorithmName.SHA256, CertificateRequestLoadOptions.Default, RSASignaturePadding.Pkcs1);
        var issuer = environment == ArcaEnvironment.Production ? production : testing;
        using var certificate = request.Create(issuer, notAfterUtc.AddYears(-2), notAfterUtc, RandomNumberGenerator.GetBytes(8));
        return certificate.ExportCertificatePem();
    }

    public void Dispose()
    {
        testing.Dispose();
        production.Dispose();
    }

    private static X509Certificate2 CreateAuthority(string commonName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={commonName}, O=AFIP, C=AR", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        return request.CreateSelfSigned(ValidFrom, ValidUntil);
    }
}
