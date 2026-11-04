using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;

/// <summary>
/// La conexión, sus certificados y sus tickets. Toda escritura del módulo toma antes el lock de la conexión, también las de
/// las familias; el del ticket ordena solo su renovación.
/// </summary>
public interface IArcaConnectionRepository
{
    Task LockConnectionAsync(CancellationToken cancellationToken);
    Task LockTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken);
    Task<ArcaConnection?> GetConnectionAsync(CancellationToken cancellationToken);
    Task<ArcaCertificate?> GetCertificateAsync(Guid connectionId, ArcaCertificateRole role, CancellationToken cancellationToken);
    Task<ArcaAccessTicket?> GetTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken);
    void Add(ArcaConnection connection);
    void Add(ArcaCertificate certificate);
    void Add(ArcaAccessTicket ticket);

    /// <summary>Se lleva en cascada los certificados, los tickets y lo que guardó cada familia.</summary>
    void Remove(ArcaConnection connection);

    /// <summary>Se lleva en cascada los tickets del certificado.</summary>
    void Remove(ArcaCertificate certificate);
}
