using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// La conexión con ARCA tal como la ve la pantalla: sin conexión, el estado es null y la revisión 0, pero los servicios
/// instalados y la plataforma configurada vienen igual, para el alta. Los certificados van sin clave ni pedido. En modo
/// delegación, <paramref name="PlatformCuit"/> es la CUIT de la plataforma que guardó la conexión.
/// </summary>
public sealed record ArcaConnectionResponse(
    ArcaConnectionStatus? Status,
    ArcaAccessMode? Mode,
    ArcaEnvironment? Environment,
    string? Cuit,
    string? PlatformCuit,
    ArcaPlatformResponse? Platform,
    IReadOnlyList<ArcaInstalledServiceResponse> InstalledServices,
    IReadOnlyList<string> EnabledServices,
    IReadOnlyList<ArcaCertificateResponse> Certificates,
    bool? LastTestPassed,
    DateTime? LastTestedAtUtc,
    long Revision)
{
    public static ArcaConnectionResponse From(ArcaConnectionRow? row, IEnumerable<ArcaInstalledService> installed, ArcaPlatformResponse? platform)
    {
        IReadOnlyList<ArcaInstalledServiceResponse> services =
            [.. installed.Select(service => new ArcaInstalledServiceResponse(service.Code.Value, service.Family, service.Required, service.Delegated))];
        return row is null
            ? new(null, null, null, null, null, platform, services, [], [], null, null, 0)
            : new(row.Status, row.Mode, row.Environment, row.Cuit, row.PlatformCuit, platform, services, row.EnabledServices,
                [.. row.Certificates.Select(certificate => new ArcaCertificateResponse(certificate.Role, certificate.Alias, certificate.IsLoaded,
                    certificate.NotBeforeUtc, certificate.NotAfterUtc, certificate.Thumbprint))],
                row.LastTestPassed, row.LastTestedAtUtc, row.Revision);
    }

    public override string ToString() =>
        $"ArcaConnectionResponse {{ cuit: {(Cuit is null ? null : CuitNumber.Mask(Cuit))}, status: {Status}, revision: {Revision} }}";
}

/// <summary>Un servicio instalado; <paramref name="Delegated"/>, si en modo delegación la empresa lo delega en la plataforma.</summary>
public sealed record ArcaInstalledServiceResponse(string Code, string Family, bool Required, bool Delegated);

/// <summary>La plataforma configurada en la instalación, en la que la empresa puede delegar.</summary>
public sealed record ArcaPlatformResponse(string Cuit, string Name);

public sealed record ArcaCertificateResponse(ArcaCertificateRole Role, string Alias, bool IsLoaded, DateTime? NotBeforeUtc,
    DateTime? NotAfterUtc, string? Thumbprint);
