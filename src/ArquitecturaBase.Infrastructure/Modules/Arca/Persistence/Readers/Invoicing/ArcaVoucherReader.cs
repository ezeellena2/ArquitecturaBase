using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.Readers.Invoicing;

internal sealed class ArcaVoucherReader(ApplicationDbContext dbContext) : IArcaVoucherReader
{
    public async Task<IReadOnlyList<ArcaVoucherSequenceRow>> ListDueSequencesAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken)
    {
        var due = await dbContext.Set<ArcaVoucher>().AsNoTracking()
            .Where(x => (x.State == ArcaVoucherState.Queued || x.State == ArcaVoucherState.Uncertain || x.State == ArcaVoucherState.Sending)
                && x.NextAttemptAtUtc <= nowUtc)
            .GroupBy(x => new { x.Environment, x.Cuit, x.PointOfSale, x.VoucherType })
            .Select(group => new { group.Key.Environment, group.Key.Cuit, group.Key.PointOfSale, group.Key.VoucherType, Oldest = group.Min(x => x.NextAttemptAtUtc) })
            .OrderBy(x => x.Oldest)
            .Take(limit)
            .ToListAsync(cancellationToken);
        return [.. due.Select(x => new ArcaVoucherSequenceRow(x.Environment, x.Cuit, x.PointOfSale, x.VoucherType))];
    }

    public async Task<IReadOnlyList<Guid>> ListDuePdfsAsync(DateTime nowUtc, int limit, CancellationToken cancellationToken) =>
        await dbContext.Set<ArcaVoucher>().AsNoTracking()
            .Where(x => x.State == ArcaVoucherState.Authorized && x.PdfStorageKey == null && x.NextAttemptAtUtc <= nowUtc)
            .OrderBy(x => x.NextAttemptAtUtc)
            .Select(x => x.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListExistingIdsAsync(IReadOnlyCollection<Guid> voucherIds, CancellationToken cancellationToken) =>
        await dbContext.Set<ArcaVoucher>().AsNoTracking().Where(x => voucherIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ArcaVoucherRow>> ListByRequestsAsync(IReadOnlyCollection<Guid> requestKeys, CancellationToken cancellationToken) =>
        await dbContext.Set<ArcaVoucher>().AsNoTracking()
            .Where(x => requestKeys.Contains(x.RequestKey))
            .Select(x => new ArcaVoucherRow(x.Id, x.RequestKey, x.State, x.ReviewReason, x.Class, x.Kind, x.VoucherType, x.PointOfSale, x.Number, x.Cae,
                x.CaeDueOn, x.IssuedOn, x.RequestedOn, x.Total, x.FailureCode, x.WaitedForArca, x.NextAttemptAtUtc, x.Revision, x.PdfStorageKey,
                x.PdfSha256, x.PdfSize, x.PayloadJson))
            .ToListAsync(cancellationToken);
}
