using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// Un certificado cargado con lo que hace falta para firmar: la clave va cifrada, como la de IA en su fila. En los modos de
/// delegación trae la CUIT de la plataforma, que es la del certificado.
/// </summary>
public sealed record ArcaSigningCertificateRow(Guid ConnectionId, Guid CertificateId, string Cuit, ArcaEnvironment Environment,
    string ProtectedPrivateKey, string CertificatePem, string? PlatformCuit = null)
{
    /// <summary>El certificado para una familia, con los servicios que la empresa tiene que haber delegado.</summary>
    public ArcaCertificateRef ToRef(IReadOnlySet<string>? delegatedServices = null) =>
        new(ConnectionId, CertificateId, Cuit, Environment, ProtectedPrivateKey, CertificatePem, PlatformCuit, delegatedServices);

    public override string ToString() => $"ArcaSigningCertificateRow {{ id: {CertificateId}, privateKey: [REDACTED] }}";
}
