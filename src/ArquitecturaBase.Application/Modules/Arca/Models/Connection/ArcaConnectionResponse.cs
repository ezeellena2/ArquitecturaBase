using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// La conexión con ARCA tal como la ve la pantalla: sin conexión, el estado es null y la revisión 0, pero los servicios
/// instalados y la plataforma configurada vienen igual, para el alta. Los certificados van sin clave ni pedido. En modo
/// delegación, <paramref name="PlatformCuit"/> es la CUIT de la plataforma que guardó la conexión.
/// <paramref name="CertificateNoticeDays"/> dice desde cuántos días antes del vencimiento se avisa (RC7-B): el de la conexión, o
/// el valor por defecto sin conexión.
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
    long Revision,
    int CertificateNoticeDays)
{
    /// <summary>
    /// La conexión como la ve la pantalla. El vencimiento del certificado se calcula al leer (B4): una conexión activa con el
    /// certificado activo vencido requiere atención, sin esperar otra prueba.
    /// </summary>
    public static ArcaConnectionResponse From(ArcaConnectionRow? row, IEnumerable<ArcaInstalledService> installed, ArcaPlatformResponse? platform,
        DateTime nowUtc)
    {
        IReadOnlyList<ArcaInstalledServiceResponse> services =
            [.. installed.Select(service => new ArcaInstalledServiceResponse(service.Code.Value, service.Family, service.Required, service.Delegated))];
        return row is null
            ? new(null, null, null, null, null, platform, services, [], [], null, null, 0, ArcaConnection.DefaultCertificateNoticeDays)
            : new(StatusAt(row, nowUtc), row.Mode, row.Environment, row.Cuit, row.PlatformCuit, platform, services, row.EnabledServices,
                [.. row.Certificates.Select(certificate => new ArcaCertificateResponse(certificate.Role, certificate.Alias, certificate.IsLoaded,
                    certificate.NotBeforeUtc, certificate.NotAfterUtc, certificate.Thumbprint))],
                row.LastTestPassed, row.LastTestedAtUtc, row.Revision, row.CertificateNoticeDays);
    }

    private static ArcaConnectionStatus StatusAt(ArcaConnectionRow row, DateTime nowUtc) =>
        row.Status == ArcaConnectionStatus.Active
        && row.Certificates.Any(certificate => certificate.Role == ArcaCertificateRole.Active && certificate.NotAfterUtc <= nowUtc)
            ? ArcaConnectionStatus.NeedsAttention : row.Status;

    public override string ToString() =>
        $"ArcaConnectionResponse {{ cuit: {(Cuit is null ? null : CuitNumber.Mask(Cuit))}, status: {Status}, revision: {Revision} }}";
}

/// <summary>Un servicio instalado; <paramref name="Delegated"/>, si en modo delegación la empresa lo delega en la plataforma.</summary>
public sealed record ArcaInstalledServiceResponse(string Code, string Family, bool Required, bool Delegated);

/// <summary>La plataforma configurada en la instalación, en la que la empresa puede delegar.</summary>
public sealed record ArcaPlatformResponse(string Cuit, string Name);

public sealed record ArcaCertificateResponse(ArcaCertificateRole Role, string Alias, bool IsLoaded, DateTime? NotBeforeUtc,
    DateTime? NotAfterUtc, string? Thumbprint);
