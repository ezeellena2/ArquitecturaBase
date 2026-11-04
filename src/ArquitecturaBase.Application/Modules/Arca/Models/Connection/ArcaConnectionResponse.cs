using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// La conexión con ARCA tal como la ve la pantalla: sin conexión, el estado es null y la revisión 0, pero los servicios
/// instalados vienen igual, para el alta. Los certificados van sin clave ni pedido.
/// </summary>
public sealed record ArcaConnectionResponse(
    ArcaConnectionStatus? Status,
    ArcaAccessMode? Mode,
    ArcaEnvironment? Environment,
    string? Cuit,
    IReadOnlyList<ArcaInstalledServiceResponse> InstalledServices,
    IReadOnlyList<string> EnabledServices,
    IReadOnlyList<ArcaCertificateResponse> Certificates,
    bool? LastTestPassed,
    DateTime? LastTestedAtUtc,
    long Revision)
{
    public static ArcaConnectionResponse From(ArcaConnectionRow? row, IEnumerable<ArcaInstalledService> installed)
    {
        IReadOnlyList<ArcaInstalledServiceResponse> services =
            [.. installed.Select(service => new ArcaInstalledServiceResponse(service.Code.Value, service.Family, service.Required))];
        return row is null
            ? new(null, null, null, null, services, [], [], null, null, 0)
            : new(row.Status, row.Mode, row.Environment, row.Cuit, services, row.EnabledServices,
                [.. row.Certificates.Select(certificate => new ArcaCertificateResponse(certificate.Role, certificate.Alias, certificate.IsLoaded,
                    certificate.NotBeforeUtc, certificate.NotAfterUtc, certificate.Thumbprint))],
                row.LastTestPassed, row.LastTestedAtUtc, row.Revision);
    }

    public override string ToString() =>
        $"ArcaConnectionResponse {{ cuit: {(Cuit is null ? null : CuitNumber.Mask(Cuit))}, status: {Status}, revision: {Revision} }}";
}

public sealed record ArcaInstalledServiceResponse(string Code, string Family, bool Required);

public sealed record ArcaCertificateResponse(ArcaCertificateRole Role, string Alias, bool IsLoaded, DateTime? NotBeforeUtc,
    DateTime? NotAfterUtc, string? Thumbprint);
