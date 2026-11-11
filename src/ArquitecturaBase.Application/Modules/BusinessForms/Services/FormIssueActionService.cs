using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The issuer is asked first: enqueueing is idempotent by run and local to the issuer, so a run that lost its outcome asks again
/// without issuing twice. Only a response that still stands is queued; once queued, it is followed to its end. While it waits
/// for its provider, whoever loaded the response is told once, by the first channel that reaches them: the channel keeps one
/// message per run and person.
/// </summary>
internal sealed class FormIssueActionService(IEnumerable<IStructuredRecordIssuer> issuers, IFormResponseReader records, IFormRuleReader definitions,
    IEnumerable<IStructuredRecordActionChannel> channels, IFormActionRecipientReader recipients, IFormCatalogItemCodeReader codes) : IFormIssueActionService
{
    public async Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work, CancellationToken cancellationToken)
    {
        if (issuers.FirstOrDefault(issuer => issuer.IssuerId == work.Snapshot.IssuerId) is not { } issuer)
            return new(FormActionRunState.Failed, FormActionErrors.IssuerUnavailable.Code, true);
        if (work.Snapshot.ReversesRunId is { } issuedRunId) return await ReverseAsync(work, issuer, issuedRunId, cancellationToken);
        var status = await issuer.GetAsync(work.RunId, cancellationToken);
        if (status.State == StructuredIssueState.NotFound)
        {
            if (!work.RecordStands) return new(FormActionRunState.NotApplicable);
            if (await RequestAsync(work, issuer, cancellationToken) is not { } request)
                return new(FormActionRunState.Failed, FormActionErrors.IssueDataUnavailable.Code);
            status = await issuer.EnqueueAsync(request, cancellationToken);
        }
        // The person decided on the issuance as the run last saw it: a newer revision makes the issuer refuse and ask again.
        if (status.State == StructuredIssueState.NeedsReview && work.DeliveryReview is { } review)
            status = await issuer.ReviewAsync(work.RunId, new StructuredIssueReview(work.PriorIssue?.Revision ?? status.Revision, review.WasSent,
                review.ReviewedByUserId, review.ReviewedAtUtc), cancellationToken);
        if (status is { State: StructuredIssueState.Pending, WaitingForProvider: true, WaitingNotice: { } notice })
            await NoticeAsync(work, notice, cancellationToken);
        return Outcome(status);
    }

    // NC1-A: what was never issued is cancelled or left alone, and the response is voided; what was issued is reversed for its
    // whole amount, and the response is voided when the reversal is issued. One with a number still in flight waits for its end.
    private static async Task<FormActionOutcomeResponse> ReverseAsync(FormActionWorkResponse work, IStructuredRecordIssuer issuer, Guid issuedRunId,
        CancellationToken cancellationToken)
    {
        var issued = await issuer.GetAsync(issuedRunId, cancellationToken);
        if (issued.State is StructuredIssueState.Pending or StructuredIssueState.NeedsReview)
            issued = await issuer.CancelAsync(issuedRunId, cancellationToken);
        switch (issued.State)
        {
            case StructuredIssueState.NotFound or StructuredIssueState.Rejected or StructuredIssueState.Cancelled:
                return new(FormActionRunState.NotApplicable);
            case StructuredIssueState.Pending or StructuredIssueState.NeedsReview:
                return new(FormActionRunState.Running, Issue: issued with { Result = null, Document = null, WaitingNotice = null });
        }
        var reversal = await issuer.GetAsync(work.RunId, cancellationToken);
        if (reversal.State == StructuredIssueState.NotFound)
            reversal = await issuer.EnqueueReversalAsync(work.RunId, issuedRunId, cancellationToken);
        return Outcome(reversal with { WaitingNotice = null });
    }

    private async Task NoticeAsync(FormActionWorkResponse work, IReadOnlyDictionary<string, string> notice, CancellationToken cancellationToken)
    {
        var record = await records.FindRecordAsync(work.RecordId, cancellationToken)
            ?? throw new InvalidOperationException("The action response is missing.");
        var author = record.Actor.UserId;
        foreach (var channel in channels)
        {
            if (!await channel.CanReachAsync(author, cancellationToken)) continue;
            if ((await channel.GetAsync(work.RunId, author, cancellationToken)).State != StructuredRecordActionState.NotFound) return;
            var culture = (await recipients.FindAsync(author, cancellationToken))?.Culture;
            var body = culture is not null && notice.TryGetValue(culture, out var text) ? text : notice.Values.First();
            await channel.EnqueueAsync(new StructuredRecordActionMessage(work.RunId, author, body), cancellationToken);
            return;
        }
    }

    private async Task<StructuredIssueRequest?> RequestAsync(FormActionWorkResponse work, IStructuredRecordIssuer issuer, CancellationToken cancellationToken)
    {
        var record = await records.FindRecordAsync(work.RecordId, cancellationToken)
            ?? throw new InvalidOperationException("The action response is missing.");
        var definition = await definitions.FindDefinitionAsync(record.Record.DefinitionId, cancellationToken);
        if (definition?.StandardKey != issuer.StandardKey || definition.StandardBindingsJson is not { } bindings) return null;
        var bound = FormDraftPolicy.Read<Dictionary<string, Guid>>(bindings);
        var itemCodes = await codes.ListAsync(FormIssueDataPolicy.ReferencedItemIds(record.Record), cancellationToken);
        return new(work.RunId, work.RecordId, FormIssueDataPolicy.Read(record.Record, bound, definition.OriginalCulture, itemCodes));
    }

    private static FormActionOutcomeResponse Outcome(StructuredIssueResponse status) => status.State switch
    {
        StructuredIssueState.Pending => new(FormActionRunState.Running, Issue: status),
        StructuredIssueState.Issued => new(FormActionRunState.Completed, Issue: status),
        StructuredIssueState.Rejected => new(FormActionRunState.Failed, status.FailureCode ?? FormActionErrors.NotIssued.Code, status.CanRetry, Issue: status),
        StructuredIssueState.NeedsReview => new(FormActionRunState.Uncertain, status.ReviewReason ?? FormActionErrors.EffectUnconfirmed.Code, Issue: status),
        StructuredIssueState.Cancelled => new(FormActionRunState.Failed, FormActionErrors.NotIssued.Code, Issue: status),
        _ => new(FormActionRunState.Uncertain, FormActionErrors.EffectUnconfirmed.Code, Issue: status),
    };
}
