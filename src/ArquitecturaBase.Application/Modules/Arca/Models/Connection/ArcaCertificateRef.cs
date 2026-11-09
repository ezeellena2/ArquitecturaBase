using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// Lo que el WSAA y las familias necesitan del certificado activo: la clave va cifrada. <paramref name="Cuit"/> es siempre la
/// de la empresa (la de <c>Auth.Cuit</c>); en los modos de delegación, <paramref name="PlatformCuit"/> es la del certificado
/// y <paramref name="DelegatedServices"/>, los servicios cuyo ticket tiene que incluir a la empresa (ADR 0027).
/// </summary>
public sealed record ArcaCertificateRef(Guid ConnectionId, Guid Id, string Cuit, ArcaEnvironment Environment,
    string ProtectedPrivateKey, string CertificatePem, string? PlatformCuit = null, IReadOnlySet<string>? DelegatedServices = null)
{
    /// <summary>La CUIT de quien firma, que va en las consultas como <c>cuitRepresentada</c>.</summary>
    public string SignerCuit => PlatformCuit ?? Cuit;

    /// <summary>La CUIT que tiene que estar en las relaciones del ticket de ese servicio; null si no hace falta mirarlas.</summary>
    public string? RequiredRelation(ArcaServiceCode service) =>
        PlatformCuit is not null && DelegatedServices?.Contains(service.Value) == true ? Cuit : null;

    public override string ToString() => $"ArcaCertificateRef {{ id: {Id}, privateKey: [REDACTED] }}";
}
