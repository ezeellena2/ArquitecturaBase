using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

/// <summary>El CMS que pide el WSAA: el pedido adentro (no separado), firmado con SHA-256 y con el certificado del firmante.</summary>
internal static class ArcaCmsSigner
{
    public static string Sign(byte[] content, X509Certificate2 certificate, RSA privateKey)
    {
        var cms = new SignedCms(new ContentInfo(content), detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate, privateKey)
        {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"), // SHA-256
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        cms.ComputeSignature(signer);
        return Convert.ToBase64String(cms.Encode());
    }
}
