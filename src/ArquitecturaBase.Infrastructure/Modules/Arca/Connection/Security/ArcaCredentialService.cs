using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

/// <summary>
/// Clave RSA de 2048 bits y pedido PKCS#10 con el sujeto que pide ARCA (C=AR, O y CN con el alias, serialNumber
/// "CUIT …"). Los secretos van en el sobre AES-GCM de IA, con el AAD atado a la conexión, el certificado y el propósito.
/// </summary>
internal sealed class ArcaCredentialService(ArcaKeyRing ring, IOptions<ArcaOptions> options) : IArcaCredentialService
{
    private const string CertificateKeyPurpose = "certificate-key";
    private const string SerialNumberOid = "2.5.4.5";

    public ArcaCertificateRequest CreateRequest(Guid connectionId, Guid certificateId, string cuit, string alias)
    {
        using var rsa = RSA.Create(2048);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCountryOrRegion("AR");
        subject.AddOrganizationName(alias);
        subject.AddCommonName(alias);
        subject.Add(SerialNumberOid, CuitSubject(cuit), UniversalTagNumber.PrintableString);
        var request = new CertificateRequest(subject.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var pkcs8 = rsa.ExportPkcs8PrivateKey();
        try { return new ArcaCertificateRequest(request.CreateSigningRequestPem(), Protect(CertificateKeyPurpose, connectionId, certificateId, pkcs8)); }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    public Result<ArcaInspectedCertificate> Inspect(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem,
        string cuit, ArcaEnvironment environment, DateTime nowUtc)
    {
        using var certificate = Read(certificatePem);
        if (certificate is null) return ArcaConnectionErrors.CertificateUnreadable;
        var issuer = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
        if (!string.Equals(issuer, Environment(environment).IssuerCommonName, StringComparison.Ordinal))
            return ArcaConnectionErrors.CertificateEnvironmentMismatch;
        if (!MatchesKey(certificate, connectionId, certificateId, protectedPrivateKey)) return ArcaConnectionErrors.CertificateKeyMismatch;
        if (SerialNumber(certificate.SubjectName) != CuitSubject(cuit)) return ArcaConnectionErrors.CertificateCuitMismatch;
        var notAfterUtc = certificate.NotAfter.ToUniversalTime();
        if (notAfterUtc <= nowUtc) return ArcaConnectionErrors.CertificateExpired;
        return new ArcaInspectedCertificate(certificate.ExportCertificatePem(), certificate.NotBefore.ToUniversalTime(), notAfterUtc,
            certificate.GetCertHashString(HashAlgorithmName.SHA256));
    }

    public string SignLoginRequest(Guid connectionId, Guid certificateId, string protectedPrivateKey, string certificatePem, byte[] loginTicketRequest)
    {
        using var rsa = ImportPrivateKey(connectionId, certificateId, protectedPrivateKey);
        using var certificate = X509Certificate2.CreateFromPem(certificatePem);
        return ArcaCmsSigner.Sign(loginTicketRequest, certificate, rsa);
    }

    public string ProtectTicket(Guid connectionId, Guid certificateId, ArcaServiceCode service, string token, string sign)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(new[] { token, sign });
        try { return Protect(TicketPurpose(service), connectionId, certificateId, plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public (string Token, string Sign)? UnprotectTicket(Guid connectionId, Guid certificateId, ArcaServiceCode service, string protectedCredentials)
    {
        var plain = Unprotect(TicketPurpose(service), connectionId, certificateId, protectedCredentials);
        if (plain is null) return null;
        try { return JsonSerializer.Deserialize<string[]>(plain) is [var token, var sign] ? (token, sign) : null; }
        catch (JsonException) { return null; }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private static string CuitSubject(string cuit) => "CUIT " + cuit;

    private static string TicketPurpose(ArcaServiceCode service) => "access-ticket:" + service.Value;

    private static X509Certificate2? Read(string certificatePem)
    {
        try { return X509Certificate2.CreateFromPem(certificatePem); }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException) { return null; }
    }

    private static string? SerialNumber(X500DistinguishedName name) => name.EnumerateRelativeDistinguishedNames()
        .Where(part => !part.HasMultipleElements && part.GetSingleElementType().Value == SerialNumberOid)
        .Select(part => part.GetSingleElementValue())
        .FirstOrDefault();

    private bool MatchesKey(X509Certificate2 certificate, Guid connectionId, Guid certificateId, string protectedPrivateKey)
    {
        using var rsa = ImportPrivateKey(connectionId, certificateId, protectedPrivateKey);
        return rsa.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(certificate.PublicKey.ExportSubjectPublicKeyInfo());
    }

    private RSA ImportPrivateKey(Guid connectionId, Guid certificateId, string protectedPrivateKey)
    {
        var pkcs8 = Unprotect(CertificateKeyPurpose, connectionId, certificateId, protectedPrivateKey)
            ?? throw new InvalidOperationException("The ARCA certificate private key cannot be decrypted with the configured ring.");
        var rsa = RSA.Create();
        try
        {
            rsa.ImportPkcs8PrivateKey(pkcs8, out _);
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
        finally { CryptographicOperations.ZeroMemory(pkcs8); }
    }

    private ArcaEnvironmentOptions Environment(ArcaEnvironment environment) =>
        environment == ArcaEnvironment.Production ? options.Value.Production : options.Value.Testing;

    private string Protect(string purpose, Guid connectionId, Guid certificateId, byte[] plain)
    {
        var keyId = ring.ActiveKeyId;
        var key = ring.Find(keyId) ?? throw new InvalidOperationException("The active ARCA encryption key is unavailable.");
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var cipher = new byte[plain.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(keyId, purpose, connectionId, certificateId));
            return string.Join('.', "1", keyId, Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToBase64String(cipher));
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private byte[]? Unprotect(string purpose, Guid connectionId, Guid certificateId, string protectedValue)
    {
        byte[]? key = null;
        byte[]? plain = null;
        try
        {
            var parts = protectedValue.Split('.');
            if (parts.Length != 5 || parts[0] != "1") return null;
            key = ring.Find(parts[1]);
            if (key is null) return null;
            var cipher = Convert.FromBase64String(parts[4]);
            plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(Convert.FromBase64String(parts[2]), cipher, Convert.FromBase64String(parts[3]), plain,
                AssociatedData(parts[1], purpose, connectionId, certificateId));
            var result = plain;
            plain = null;
            return result;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return null; }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private byte[] AssociatedData(string keyId, string purpose, Guid connectionId, Guid certificateId) =>
        Encoding.UTF8.GetBytes($"ArquitecturaBase.Arca|1|{ring.InstallationId}|{keyId}|{purpose}|{connectionId:N}|{certificateId:N}");
}
