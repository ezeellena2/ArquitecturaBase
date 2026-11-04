using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;

public interface IArcaConnectionReader
{
    /// <summary>La conexión con sus certificados, sin claves privadas; null si la instalación no empezó una.</summary>
    Task<ArcaConnectionRow?> FindAsync(CancellationToken cancellationToken);

    Task<ArcaTicketRow?> FindTicketAsync(Guid certificateId, ArcaServiceCode service, CancellationToken cancellationToken);

    Task<ArcaPendingRequestRow?> FindPendingRequestAsync(CancellationToken cancellationToken);

    /// <summary>El certificado de ese papel si ya está cargado; null si no hay o todavía espera el archivo de ARCA.</summary>
    Task<ArcaSigningCertificateRow?> FindSigningCertificateAsync(ArcaCertificateRole role, CancellationToken cancellationToken);
}
