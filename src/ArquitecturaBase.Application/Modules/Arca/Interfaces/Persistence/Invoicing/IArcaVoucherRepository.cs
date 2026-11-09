using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;

/// <summary>La cola de emisión y sus secuencias (ADR 0028). Quien escribe toma antes el lock de la conexión.</summary>
public interface IArcaVoucherRepository
{
    Task<ArcaVoucher?> GetVoucherAsync(Guid id, CancellationToken cancellationToken);

    Task<ArcaVoucher?> GetVoucherByRequestAsync(Guid requestKey, CancellationToken cancellationToken);

    Task<ArcaVoucherSequence?> GetSequenceAsync(ArcaVoucherSequenceRow key, CancellationToken cancellationToken);

    /// <summary>
    /// El comprobante que le toca a la secuencia: su pendiente si tiene uno; si no, el más viejo en cola o incierto. Puede no
    /// estar vencido todavía: la secuencia avanza en orden y espera.
    /// </summary>
    Task<ArcaVoucher?> GetNextAsync(ArcaVoucherSequence sequence, CancellationToken cancellationToken);

    void Add(ArcaVoucher voucher);

    void Add(ArcaVoucherSequence sequence);
}
