using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;

/// <summary>Lecturas de la cola de emisión, sin seguimiento.</summary>
public interface IArcaVoucherReader
{
    /// <summary>Las secuencias con un comprobante en cola o incierto cuya espera ya pasó, de la más atrasada a la más nueva.</summary>
    Task<IReadOnlyList<ArcaVoucherSequenceRow>> ListDueSequencesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);

    /// <summary>Los comprobantes autorizados que todavía no tienen su PDF fiscal y ya pueden intentarlo.</summary>
    Task<IReadOnlyList<Guid>> ListDuePdfsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<ArcaVoucherRow>> ListByRequestsAsync(IReadOnlyCollection<Guid> requestKeys, CancellationToken cancellationToken);
}
