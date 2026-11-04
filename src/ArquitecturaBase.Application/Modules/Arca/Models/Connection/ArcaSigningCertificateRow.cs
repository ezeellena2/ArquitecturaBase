using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Un certificado cargado con lo que hace falta para firmar: la clave va cifrada, como la de IA en su fila.</summary>
public sealed record ArcaSigningCertificateRow(Guid ConnectionId, Guid CertificateId, string Cuit, ArcaEnvironment Environment,
    string ProtectedPrivateKey, string CertificatePem)
{
    public ArcaCertificateRef ToRef() => new(ConnectionId, CertificateId, Cuit, Environment, ProtectedPrivateKey, CertificatePem);

    public override string ToString() => $"ArcaSigningCertificateRow {{ id: {CertificateId}, privateKey: [REDACTED] }}";
}
