using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.Arca;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

public sealed class ArcaCredentialServiceTests : IDisposable
{
    private const string Cuit = "20000000060";
    private static readonly DateTime NowUtc = new(2026, 11, 3, 13, 0, 0, DateTimeKind.Utc);
    private static readonly ArcaServiceCode Invoicing = ArcaServiceCode.Create("wsfe")!.Value;
    private static readonly ArcaServiceCode Registry = ArcaServiceCode.Create("ws_sr_constancia_inscripcion")!.Value;

    private readonly FakeArcaCertificateAuthority authority = new();
    private readonly ArcaCredentialService service = Service();
    private readonly Guid connectionId = Guid.CreateVersion7();
    private readonly Guid certificateId = Guid.CreateVersion7();

    [Fact]
    public void The_request_names_the_alias_and_the_cuit_and_its_signature_verifies()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");

        // LoadSigningRequestPem verifica la firma del pedido.
        var loaded = CertificateRequest.LoadSigningRequestPem(request.RequestPem, HashAlgorithmName.SHA256);
        var subject = loaded.SubjectName.EnumerateRelativeDistinguishedNames()
            .ToDictionary(name => name.GetSingleElementType().Value!, name => name.GetSingleElementValue());
        Assert.Equal("AR", subject["2.5.4.6"]);
        Assert.Equal("plataforma", subject["2.5.4.3"]);
        Assert.Equal("CUIT 20000000060", subject["2.5.4.5"]);
        Assert.DoesNotContain(request.ProtectedPrivateKey, request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_private_key_is_protected_and_bound_to_its_connection_and_certificate()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");
        var certificate = authority.Issue(request.RequestPem, ArcaEnvironment.Testing, NowUtc.AddYears(2));

        Assert.StartsWith("1.one.", request.ProtectedPrivateKey, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", request.ProtectedPrivateKey, StringComparison.Ordinal);
        Assert.True(Inspect(request.ProtectedPrivateKey, certificate).IsSuccess);
        Assert.Throws<InvalidOperationException>(() => service.Inspect(
            Guid.CreateVersion7(), certificateId, request.ProtectedPrivateKey, certificate, Cuit, ArcaEnvironment.Testing, NowUtc));
        Assert.Throws<InvalidOperationException>(() => service.Inspect(
            connectionId, Guid.CreateVersion7(), request.ProtectedPrivateKey, certificate, Cuit, ArcaEnvironment.Testing, NowUtc));
    }

    [Fact]
    public void A_certificate_issued_for_the_request_is_accepted_with_its_dates_and_thumbprint()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");
        var notAfterUtc = NowUtc.AddYears(2);
        var pem = "Bag Attributes\n" + authority.Issue(request.RequestPem, ArcaEnvironment.Testing, notAfterUtc);

        var inspected = Inspect(request.ProtectedPrivateKey, pem).Value;

        using var certificate = X509Certificate2.CreateFromPem(pem);
        Assert.Equal(certificate.ExportCertificatePem(), inspected.CertificatePem);
        Assert.Equal(notAfterUtc.AddYears(-2), inspected.NotBeforeUtc);
        Assert.Equal(notAfterUtc, inspected.NotAfterUtc);
        Assert.Equal(DateTimeKind.Utc, inspected.NotAfterUtc.Kind);
        Assert.Equal(certificate.GetCertHashString(HashAlgorithmName.SHA256), inspected.Thumbprint);
    }

    [Fact]
    public void A_certificate_for_another_request_does_not_match_the_key()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");
        var other = service.CreateRequest(connectionId, Guid.CreateVersion7(), Cuit, "plataforma");

        var result = Inspect(request.ProtectedPrivateKey, authority.Issue(other.RequestPem, ArcaEnvironment.Testing, NowUtc.AddYears(2)));

        Assert.Equal(ArcaConnectionErrors.CertificateKeyMismatch, result.Error);
    }

    [Fact]
    public void A_certificate_for_another_cuit_is_rejected()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");
        var certificate = authority.Issue(request.RequestPem, ArcaEnvironment.Testing, NowUtc.AddYears(2));

        var result = service.Inspect(connectionId, certificateId, request.ProtectedPrivateKey, certificate, "30000000007", ArcaEnvironment.Testing, NowUtc);

        Assert.Equal(ArcaConnectionErrors.CertificateCuitMismatch, result.Error);
    }

    [Fact]
    public void A_certificate_from_the_other_environment_is_rejected()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");

        var result = Inspect(request.ProtectedPrivateKey, authority.Issue(request.RequestPem, ArcaEnvironment.Production, NowUtc.AddYears(2)));

        Assert.Equal(ArcaConnectionErrors.CertificateEnvironmentMismatch, result.Error);
    }

    [Fact]
    public void An_expired_certificate_is_rejected()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");

        var result = Inspect(request.ProtectedPrivateKey, authority.Issue(request.RequestPem, ArcaEnvironment.Testing, NowUtc));

        Assert.Equal(ArcaConnectionErrors.CertificateExpired, result.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es un certificado")]
    [InlineData("-----BEGIN CERTIFICATE-----\nAAAA\n-----END CERTIFICATE-----")]
    public void Text_that_is_not_a_certificate_is_unreadable(string pem)
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");

        Assert.Equal(ArcaConnectionErrors.CertificateUnreadable, Inspect(request.ProtectedPrivateKey, pem).Error);
    }

    [Fact]
    public void The_login_request_is_signed_with_the_certificate_and_carries_the_request()
    {
        var request = service.CreateRequest(connectionId, certificateId, Cuit, "plataforma");
        var certificatePem = authority.Issue(request.RequestPem, ArcaEnvironment.Testing, NowUtc.AddYears(2));
        var content = Encoding.UTF8.GetBytes("<loginTicketRequest version=\"1.0\"/>");

        var cms = new SignedCms();
        cms.Decode(Convert.FromBase64String(service.SignLoginRequest(connectionId, certificateId, request.ProtectedPrivateKey, certificatePem, content)));

        cms.CheckSignature(verifySignatureOnly: true);
        Assert.Equal(content, cms.ContentInfo.Content);
        using var certificate = X509Certificate2.CreateFromPem(certificatePem);
        Assert.Equal(certificate.Thumbprint, Assert.Single(cms.SignerInfos.Cast<SignerInfo>()).Certificate?.Thumbprint);
    }

    [Fact]
    public void A_ticket_is_protected_for_its_connection_certificate_and_service()
    {
        var protectedTicket = service.ProtectTicket(connectionId, certificateId, Invoicing, "the-token", "the-sign");

        Assert.DoesNotContain("the-token", protectedTicket, StringComparison.Ordinal);
        Assert.Equal(("the-token", "the-sign"), service.UnprotectTicket(connectionId, certificateId, Invoicing, protectedTicket));
        Assert.Null(service.UnprotectTicket(connectionId, certificateId, Registry, protectedTicket));
        Assert.Null(service.UnprotectTicket(connectionId, Guid.CreateVersion7(), Invoicing, protectedTicket));
        Assert.Null(service.UnprotectTicket(Guid.CreateVersion7(), certificateId, Invoicing, protectedTicket));
        Assert.Null(service.UnprotectTicket(connectionId, certificateId, Invoicing, protectedTicket[..^3] + "abc"));
    }

    public void Dispose() => authority.Dispose();

    private Result<ArcaInspectedCertificate> Inspect(string protectedPrivateKey, string certificatePem) =>
        service.Inspect(connectionId, certificateId, protectedPrivateKey, certificatePem, Cuit, ArcaEnvironment.Testing, NowUtc);

    private static ArcaCredentialService Service()
    {
        var ring = new ArcaSecretProtectionOptions
        {
            InstallationId = "test-installation",
            ActiveKeyId = "one",
            Keys = new Dictionary<string, string> { ["one"] = Convert.ToBase64String(new byte[32]) },
        };
        var options = new ArcaOptions
        {
            Testing = new ArcaEnvironmentOptions { IssuerCommonName = FakeArcaCertificateAuthority.TestingCommonName },
            Production = new ArcaEnvironmentOptions { IssuerCommonName = FakeArcaCertificateAuthority.ProductionCommonName },
        };
        return new ArcaCredentialService(new ArcaKeyRing(Options.Create(ring)), Options.Create(options));
    }
}
