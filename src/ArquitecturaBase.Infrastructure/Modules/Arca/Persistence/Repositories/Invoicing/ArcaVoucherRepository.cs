using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Repositories.Invoicing;

internal sealed class ArcaVoucherRepository(ApplicationDbContext dbContext) : IArcaVoucherRepository
{
    public Task<ArcaVoucher?> GetVoucherAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaVoucher>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<ArcaVoucher?> GetVoucherByRequestAsync(Guid requestKey, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaVoucher>().SingleOrDefaultAsync(x => x.RequestKey == requestKey, cancellationToken);

    public Task<ArcaVoucherSequence?> GetSequenceAsync(ArcaVoucherSequenceRow key, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaVoucherSequence>().SingleOrDefaultAsync(x => x.Environment == key.Environment && x.Cuit == key.Cuit
            && x.PointOfSale == key.PointOfSale && x.VoucherType == key.VoucherType, cancellationToken);

    public async Task<ArcaVoucher?> GetNextAsync(ArcaVoucherSequence sequence, CancellationToken cancellationToken)
    {
        if (sequence.PendingVoucherId is { } pending) return await GetVoucherAsync(pending, cancellationToken);
        return await dbContext.Set<ArcaVoucher>()
            .Where(x => x.Environment == sequence.Environment && x.Cuit == sequence.Cuit && x.PointOfSale == sequence.PointOfSale
                && x.VoucherType == sequence.VoucherType && (x.State == ArcaVoucherState.Queued || x.State == ArcaVoucherState.Uncertain))
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> ExistsCreditNoteAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        dbContext.Set<ArcaVoucher>().AnyAsync(x => x.CancelsVoucherId == invoiceId
            && x.State != ArcaVoucherState.Rejected && x.State != ArcaVoucherState.Cancelled, cancellationToken);

    public void Add(ArcaVoucher voucher) { dbContext.RequireTransaction(); dbContext.Set<ArcaVoucher>().Add(voucher); }

    public void Add(ArcaVoucherSequence sequence) { dbContext.RequireTransaction(); dbContext.Set<ArcaVoucherSequence>().Add(sequence); }
}
