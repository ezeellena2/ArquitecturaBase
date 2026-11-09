using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

public sealed record ArcaConnectionRow(
    Guid Id,
    ArcaAccessMode Mode,
    ArcaEnvironment Environment,
    string Cuit,
    string? PlatformCuit,
    ArcaConnectionStatus Status,
    IReadOnlyList<string> EnabledServices,
    bool? LastTestPassed,
    DateTime? LastTestedAtUtc,
    long Revision,
    IReadOnlyList<ArcaCertificateRow> Certificates)
{
    public override string ToString() => $"ArcaConnectionRow {{ cuit: {CuitNumber.Mask(Cuit)}, status: {Status} }}";
}

/// <summary>Un certificado de la conexión, sin la clave privada ni el pedido.</summary>
public sealed record ArcaCertificateRow(
    Guid Id,
    ArcaCertificateRole Role,
    string Alias,
    bool IsLoaded,
    DateTime? NotBeforeUtc,
    DateTime? NotAfterUtc,
    string? Thumbprint);
