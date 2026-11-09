using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;

/// <summary>Leer las emisiones de una lista de pedidos y lo que decide una persona sobre una ya encolada.</summary>
public interface IArcaVoucherReviewService
{
    Task<IReadOnlyList<ArcaVoucherResponse>> ListAsync(IReadOnlyCollection<Guid> requestKeys, CancellationToken cancellationToken);

    Task<Result<ArcaVoucherResponse>> ReviewAsync(Guid requestKey, ArcaVoucherReviewDecision decision, int expectedRevision, CancellationToken cancellationToken);

    /// <summary>Sólo mientras no tiene número.</summary>
    Task<Result<ArcaVoucherResponse>> CancelAsync(Guid requestKey, CancellationToken cancellationToken);
}
