using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;

/// <summary>
/// El alta, los servicios elegidos, el certificado y su renovación. Cada escritura pide la revisión que vio la pantalla y
/// toma el lock de la conexión; una revisión vieja da <c>Arca.Connection.RevisionConflict</c>.
/// </summary>
public interface IArcaConnectionService
{
    Task<Result<ArcaConnectionResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Genera la clave y el pedido de certificado. Sin conexión, la crea con la CUIT, el ambiente y los servicios; con una,
    /// pide otro certificado para ella (una renovación o un pedido nuevo en el alta) y reemplaza el pedido pendiente.
    /// </summary>
    Task<Result<ArcaConnectionResponse>> StartCertificateRequestAsync(string cuit, ArcaEnvironment environment, string alias,
        IReadOnlyCollection<string> services, long expectedRevision, CancellationToken cancellationToken);

    Task<Result<ArcaConnectionResponse>> ChooseServicesAsync(IReadOnlyCollection<string> services, long expectedRevision,
        CancellationToken cancellationToken);

    Task<Result<ArcaPendingRequestResponse>> GetPendingRequestAsync(CancellationToken cancellationToken);

    Task<Result<ArcaConnectionResponse>> CancelRenewalAsync(long expectedRevision, CancellationToken cancellationToken);

    /// <summary>
    /// Revisa y guarda el certificado del pedido pendiente. En el alta queda activo y la conexión sin verificar; en una
    /// renovación queda pendiente y cargado, y lo promueve la prueba en verde.
    /// </summary>
    Task<Result<ArcaConnectionResponse>> UploadCertificateAsync(string certificatePem, long expectedRevision, CancellationToken cancellationToken);

    Task<Result> DisconnectAsync(long expectedRevision, CancellationToken cancellationToken);
}
