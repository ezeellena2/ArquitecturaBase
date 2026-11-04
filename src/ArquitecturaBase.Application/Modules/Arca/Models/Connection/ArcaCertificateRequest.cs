namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>El pedido de certificado, que es público, y la clave privada cifrada con el anillo.</summary>
public sealed record ArcaCertificateRequest(string RequestPem, string ProtectedPrivateKey)
{
    public override string ToString() => "ArcaCertificateRequest { privateKey: [REDACTED] }";
}
