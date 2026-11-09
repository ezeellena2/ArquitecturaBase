using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// Lo que una persona hace con una emisión ya encolada (ADR 0028): leerla, decidir sobre una en revisión con la revisión
/// esperada, o cancelarla mientras no tiene número. Si deja de tener número, su secuencia deja de esperarla.
/// </summary>
internal sealed class ArcaVoucherReviewService(IArcaConnectionRepository connections, IArcaVoucherRepository vouchers, IArcaVoucherReader reader,
    IUnitOfWork unitOfWork, TimeProvider timeProvider, ILogger<ArcaVoucherReviewService> logger) : IArcaVoucherReviewService
{
    public Task<IReadOnlyList<ArcaVoucherResponse>> ListAsync(IReadOnlyCollection<Guid> requestKeys, CancellationToken cancellationToken) =>
        requestKeys.Count == 0 ? Task.FromResult<IReadOnlyList<ArcaVoucherResponse>>([]) : ListCoreAsync(requestKeys, cancellationToken);

    public Task<Result<ArcaVoucherResponse>> ReviewAsync(Guid requestKey, ArcaVoucherReviewDecision decision, int expectedRevision,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "ReviewArcaVoucher", () =>
            unitOfWork.ExecuteInTransactionAsync(token => ReviewCoreAsync(requestKey, decision, expectedRevision, token), CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result<ArcaVoucherResponse>> CancelAsync(Guid requestKey, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "CancelArcaVoucher", () =>
            unitOfWork.ExecuteInTransactionAsync(token => CancelCoreAsync(requestKey, token), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<IReadOnlyList<ArcaVoucherResponse>> ListCoreAsync(IReadOnlyCollection<Guid> requestKeys, CancellationToken cancellationToken) =>
        [.. (await reader.ListByRequestsAsync(requestKeys, cancellationToken)).Select(ArcaVoucherRowPolicy.Response)];

    private async Task<Result<ArcaVoucherResponse>> ReviewCoreAsync(Guid requestKey, ArcaVoucherReviewDecision decision, int expectedRevision,
        CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await vouchers.GetVoucherByRequestAsync(requestKey, cancellationToken) is not { } voucher) return ArcaInvoicingErrors.VoucherNotFound;
        var heldNumber = voucher.HoldsNumber;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var reviewed = voucher.Review(decision, expectedRevision, ArcaIssueDateWindow.Today(nowUtc), nowUtc);
        if (reviewed.IsFailure) return reviewed.Error;

        // Sin número, la secuencia deja de esperarla.
        if (heldNumber && !voucher.HoldsNumber
            && await vouchers.GetSequenceAsync(new ArcaVoucherSequenceRow(voucher.Environment, voucher.Cuit, voucher.PointOfSale, voucher.VoucherType),
                cancellationToken) is { } sequence)
            sequence.ReleasePending(voucher.Id);
        return ArcaVoucherRowPolicy.Response(voucher);
    }

    private async Task<Result<ArcaVoucherResponse>> CancelCoreAsync(Guid requestKey, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await vouchers.GetVoucherByRequestAsync(requestKey, cancellationToken) is not { } voucher) return ArcaInvoicingErrors.VoucherNotFound;
        var cancelled = voucher.Cancel(timeProvider.GetUtcNow().UtcDateTime);
        return cancelled.IsFailure ? cancelled.Error : ArcaVoucherRowPolicy.Response(voucher);
    }
}
