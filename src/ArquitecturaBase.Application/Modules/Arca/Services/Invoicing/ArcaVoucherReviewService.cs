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
        var sequenceRow = new ArcaVoucherSequenceRow(voucher.Environment, voucher.Cuit, voucher.PointOfSale, voucher.VoucherType);
        var (issueOn, payloadJson) = decision == ArcaVoucherReviewDecision.IssueToday
            ? await IssueTodayAsync(voucher, sequenceRow, ArcaIssueDateWindow.Today(nowUtc), cancellationToken)
            : (ArcaIssueDateWindow.Today(nowUtc), null);
        var reviewed = voucher.Review(decision, expectedRevision, issueOn, nowUtc, payloadJson);
        if (reviewed.IsFailure) return reviewed.Error;

        // Sin número, la secuencia deja de esperarla.
        if (heldNumber && !voucher.HoldsNumber && await vouchers.GetSequenceAsync(sequenceRow, cancellationToken) is { } sequence)
            sequence.ReleasePending(voucher.Id);
        return ArcaVoucherRowPolicy.Response(voucher);
    }

    /// <summary>
    /// «Emitir con la fecha de hoy» (ADR 0031): la de hoy o, si el último comprobante que la base conoce de la secuencia tiene una
    /// posterior, la suya, porque ARCA no acepta una anterior; y un vencimiento del pago ya pasado se corre a esa fecha, como en
    /// la nota de crédito (B2). El pedido guardado se reescribe sólo si cambia.
    /// </summary>
    private async Task<(DateOnly IssueOn, string? PayloadJson)> IssueTodayAsync(ArcaVoucher voucher, ArcaVoucherSequenceRow sequence, DateOnly today,
        CancellationToken cancellationToken)
    {
        var issueOn = await vouchers.FindLastIssuedOnAsync(sequence, cancellationToken) is { } last && last > today ? last : today;
        var payload = ArcaVoucherRequestPolicy.FromJson(voucher.PayloadJson);
        return payload.PaymentDueOn is { } due && due < issueOn
            ? (issueOn, ArcaVoucherRequestPolicy.ToJson(payload with { PaymentDueOn = issueOn }))
            : (issueOn, null);
    }

    private async Task<Result<ArcaVoucherResponse>> CancelCoreAsync(Guid requestKey, CancellationToken cancellationToken)
    {
        await connections.LockConnectionAsync(cancellationToken);
        if (await vouchers.GetVoucherByRequestAsync(requestKey, cancellationToken) is not { } voucher) return ArcaInvoicingErrors.VoucherNotFound;
        var cancelled = voucher.Cancel(timeProvider.GetUtcNow().UtcDateTime);
        return cancelled.IsFailure ? cancelled.Error : ArcaVoucherRowPolicy.Response(voucher);
    }
}
