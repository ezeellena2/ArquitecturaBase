using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;

/// <summary>
/// La conexión, sus certificados y la configuración de Facturación en memoria, con los mismos contratos que EF. Las
/// escrituras y los locks exigen estar dentro de la unidad de trabajo; borrar la conexión se lleva todo, como la cascada.
/// </summary>
internal sealed class FakeArcaStore : IArcaConnectionRepository, IArcaConnectionReader, IArcaInvoicingRepository, IArcaInvoicingReader
{
    public Func<bool> InTransaction { get; set; } = () => true;
    public ArcaConnection? Connection { get; private set; }
    public List<ArcaCertificate> Certificates { get; } = [];
    public ArcaInvoicingSettings? Settings { get; private set; }

    public ArcaCertificate? Certificate(ArcaCertificateRole role) => Certificates.SingleOrDefault(certificate => certificate.Role == role);

    public Task LockConnectionAsync(CancellationToken cancellationToken) => Locked();

    public Task LockTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken) => Locked();

    public Task<ArcaConnection?> GetConnectionAsync(CancellationToken cancellationToken) => Task.FromResult(Connection);

    public Task<ArcaCertificate?> GetCertificateAsync(Guid connectionId, ArcaCertificateRole role, CancellationToken cancellationToken) =>
        Task.FromResult(Certificates.SingleOrDefault(certificate => certificate.ConnectionId == connectionId && certificate.Role == role));

    public Task<ArcaAccessTicket?> GetTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken) =>
        Task.FromResult<ArcaAccessTicket?>(null);

    public void Add(ArcaConnection connection)
    {
        Write();
        Connection = connection;
    }

    public void Add(ArcaCertificate certificate)
    {
        Write();
        Certificates.Add(certificate);
    }

    public void Add(ArcaAccessTicket ticket) => Write();

    public void Add(ArcaInvoicingSettings settings)
    {
        Write();
        Settings = settings;
    }

    public void Remove(ArcaConnection connection)
    {
        Write();
        Connection = null;
        Certificates.Clear();
        Settings = null;
    }

    public void Remove(ArcaCertificate certificate)
    {
        Write();
        Certificates.Remove(certificate);
    }

    public Task<ArcaConnectionRow?> FindAsync(CancellationToken cancellationToken) => Task.FromResult(Connection is { } connection
        ? new ArcaConnectionRow(connection.Id, connection.Mode, connection.Environment, connection.Cuit, connection.PlatformCuit, connection.Status,
            [.. connection.EnabledServices], connection.LastTestPassed, connection.LastTestedAtUtc, connection.Revision,
            [.. Certificates.OrderBy(certificate => certificate.Role).Select(certificate => new ArcaCertificateRow(certificate.Id, certificate.Role,
                certificate.Alias, certificate.IsLoaded, certificate.NotBeforeUtc, certificate.NotAfterUtc, certificate.Thumbprint))])
        : null);

    public Task<ArcaTicketRow?> FindTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken) =>
        Task.FromResult<ArcaTicketRow?>(null);

    public Task<ArcaPendingRequestRow?> FindPendingRequestAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Certificate(ArcaCertificateRole.Pending) is { } pending ? new ArcaPendingRequestRow(pending.Alias, pending.RequestPem) : null);

    public Task<ArcaSigningCertificateRow?> FindSigningCertificateAsync(ArcaCertificateRole role, CancellationToken cancellationToken) =>
        Task.FromResult(Connection is { } connection && Certificate(role) is { IsLoaded: true } certificate
            ? new ArcaSigningCertificateRow(connection.Id, certificate.Id, connection.Cuit, connection.Environment,
                certificate.ProtectedPrivateKey, certificate.CertificatePem!, connection.PlatformCuit)
            : null);

    public Task<ArcaInvoicingSettings?> GetSettingsAsync(Guid connectionId, CancellationToken cancellationToken) =>
        Task.FromResult(Settings?.ConnectionId == connectionId ? Settings : null);

    public Task<ArcaInvoicingSettingsRow?> FindSettingsAsync(Guid connectionId, CancellationToken cancellationToken) =>
        Task.FromResult(Settings?.ConnectionId == connectionId ? new ArcaInvoicingSettingsRow(Settings.PointOfSale, Settings.GrossIncomeNumber, Settings.ActivityStartOn) : null);

    private Task Locked()
    {
        Assert.True(InTransaction(), "ARCA locks are taken inside the unit of work.");
        return Task.CompletedTask;
    }

    private void Write() => Assert.True(InTransaction(), "ARCA writes happen inside the unit of work.");
}
