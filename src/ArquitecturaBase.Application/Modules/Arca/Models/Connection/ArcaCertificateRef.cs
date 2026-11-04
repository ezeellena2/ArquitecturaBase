using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Lo que el WSAA y las familias necesitan del certificado activo: la clave va cifrada.</summary>
public sealed record ArcaCertificateRef(Guid ConnectionId, Guid Id, string Cuit, ArcaEnvironment Environment,
    string ProtectedPrivateKey, string CertificatePem)
{
    public override string ToString() => $"ArcaCertificateRef {{ id: {Id}, privateKey: [REDACTED] }}";
}
