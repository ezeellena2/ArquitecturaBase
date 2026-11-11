using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// ARCA as the issuer of the «IssueDocument» rule action (ADR 0028): the run is the key of the request, so enqueueing again
/// returns the same voucher. It never calls ARCA: the queue's worker does. A request the queue does not take comes back rejected
/// with its code and nothing queued.
/// </summary>
internal sealed class ArcaStructuredRecordIssuer(IArcaVoucherService vouchers, IArcaVoucherReviewService reviews, TimeProvider timeProvider,
    IEnumerable<IStructuredPartyLookup> lookups) : IStructuredRecordIssuer
{
    public string IssuerId => ArcaInvoiceStandard.Key;
    public IReadOnlyDictionary<string, string> Name => ArcaInvoiceIssuePolicy.Name;
    public string StandardKey => ArcaInvoiceStandard.Key;
    public string ReviewPermission => ArcaPermissions.Issue;
    public IReadOnlyList<StructuredRecordResultField> ResultFields => ArcaInvoiceIssuePolicy.ResultFields;

    public async Task<StructuredIssuePreview> PreviewAsync(IReadOnlyList<StructuredIssueDatum> data, CancellationToken cancellationToken)
    {
        var completed = await CustomerAsync(data, cancellationToken);
        if (completed.IsFailure) return new(null, null, null, completed.Error);
        var order = ArcaInvoiceOrderPolicy.Order(new StructuredIssueRequest(Guid.Empty, Guid.Empty, completed.Value), ArcaIssueDateWindow.Today(timeProvider.GetUtcNow().UtcDateTime));
        if (order.IsFailure) return new(null, null, null, order.Error.Code);
        var preview = await vouchers.PreviewAsync(order.Value, cancellationToken);
        return preview.IsSuccess ? ArcaInvoiceIssuePolicy.Preview(preview.Value, order.Value) : new(null, null, null, preview.Error.Code);
    }

    public async Task<StructuredIssueResponse> EnqueueAsync(StructuredIssueRequest request, CancellationToken cancellationToken)
    {
        var completed = await CustomerAsync(request.Data, cancellationToken);
        if (completed.IsFailure) return new(StructuredIssueState.Rejected, completed.Error);
        var order = ArcaInvoiceOrderPolicy.Order(request with { Data = completed.Value }, ArcaIssueDateWindow.Today(timeProvider.GetUtcNow().UtcDateTime));
        if (order.IsFailure) return new(StructuredIssueState.Rejected, order.Error.Code);
        var queued = await vouchers.EnqueueAsync(order.Value, cancellationToken);
        return queued.IsSuccess ? ArcaInvoiceIssuePolicy.Response(queued.Value) : new(StructuredIssueState.Rejected, queued.Error.Code, CanRetry: true);
    }

    // «Otro: escribir la CUIT»: the registry completes the customer from the CUIT written in the invoice, so the «¿La emito?» already
    // says the name ARCA gave. Any other customer goes as it came.
    private async Task<ResultOf> CustomerAsync(IReadOnlyList<StructuredIssueDatum> data, CancellationToken cancellationToken)
    {
        if (data.FirstOrDefault(datum => datum.Key == ArcaInvoiceStandard.Customer) is not { Value: ArcaInvoiceStandard.OtherCustomer }) return new(data, null);
        var taxId = data.FirstOrDefault(datum => datum.Key == ArcaInvoiceStandard.CustomerTaxId)?.Value;
        if (string.IsNullOrWhiteSpace(taxId)) return new(data, ArcaInvoicingErrors.ReceiverInvalid.Code);
        if (lookups.FirstOrDefault(lookup => lookup.CatalogKey == ArcaCustomersCatalog.Key) is not { } registry)
            return new(data, ArcaInvoicingErrors.ReceiverInvalid.Code);
        var party = await registry.FindAsync(taxId, cancellationToken);
        if (party.FailureCode is { } code) return new(data, code);
        return new(data.Select(datum => datum.Key == ArcaInvoiceStandard.Customer
            ? new StructuredIssueDatum(ArcaInvoiceStandard.Customer, ArcaInvoiceStandard.OtherCustomer, party.Name, party.Fields) : datum).ToArray(), null);
    }

    private sealed record ResultOf(IReadOnlyList<StructuredIssueDatum> Value, string? Error)
    {
        public bool IsFailure => Error is not null;
    }

    public async Task<StructuredIssueResponse> GetAsync(Guid runId, CancellationToken cancellationToken) =>
        (await reviews.ListAsync([runId], cancellationToken)).SingleOrDefault() is { } voucher
            ? ArcaInvoiceIssuePolicy.Response(voucher) : new(StructuredIssueState.NotFound);

    // Proceeding is what the reason allows: another number for a number taken or missing, the date of today for an expired one.
    public async Task<StructuredIssueResponse> ReviewAsync(Guid runId, StructuredIssueReview review, CancellationToken cancellationToken)
    {
        if ((await reviews.ListAsync([runId], cancellationToken)).SingleOrDefault() is not { } voucher) return new(StructuredIssueState.NotFound);
        if (voucher.State != ArcaVoucherState.NeedsReview || review.ExpectedRevision is < int.MinValue or > int.MaxValue)
            return ArcaInvoiceIssuePolicy.Response(voucher);
        var decision = !review.Proceed ? ArcaVoucherReviewDecision.DoNotIssue
            : voucher.ReviewReason == ArcaVoucherReviewReason.DateOutOfRange ? ArcaVoucherReviewDecision.IssueToday : ArcaVoucherReviewDecision.IssueWithNextNumber;
        var reviewed = await reviews.ReviewAsync(runId, decision, (int)review.ExpectedRevision, cancellationToken);
        return reviewed.IsSuccess ? ArcaInvoiceIssuePolicy.Response(reviewed.Value) : await GetAsync(runId, cancellationToken);
    }

    public async Task<StructuredIssueResponse> CancelAsync(Guid runId, CancellationToken cancellationToken)
    {
        var cancelled = await reviews.CancelAsync(runId, cancellationToken);
        return cancelled.IsSuccess ? ArcaInvoiceIssuePolicy.Response(cancelled.Value) : await GetAsync(runId, cancellationToken);
    }

    // The credit note of the same class for the total, with the invoice in CbtesAsoc and the date of today (NC1-A).
    public async Task<StructuredIssueResponse> EnqueueReversalAsync(Guid reversalRunId, Guid issuedRunId, CancellationToken cancellationToken)
    {
        var queued = await vouchers.EnqueueCreditNoteAsync(reversalRunId, issuedRunId, cancellationToken);
        return queued.IsSuccess ? ArcaInvoiceIssuePolicy.Response(queued.Value) : new(StructuredIssueState.Rejected, queued.Error.Code, CanRetry: true);
    }
}
