using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The issuer is asked first: enqueueing is idempotent by run and local to the issuer, so a run that lost its outcome asks again
/// without issuing twice. Only a response that still stands is queued; once queued, it is followed to its end.
/// </summary>
internal sealed class FormIssueActionService(IEnumerable<IStructuredRecordIssuer> issuers, IFormResponseReader records, IFormRuleReader definitions)
    : IFormIssueActionService
{
    public async Task<FormActionOutcomeResponse> ExecuteAsync(FormActionWorkResponse work, CancellationToken cancellationToken)
    {
        if (issuers.FirstOrDefault(issuer => issuer.IssuerId == work.Snapshot.IssuerId) is not { } issuer)
            return new(FormActionRunState.Failed, FormActionErrors.IssuerUnavailable.Code, true);
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
        return Outcome(status);
    }

    private async Task<StructuredIssueRequest?> RequestAsync(FormActionWorkResponse work, IStructuredRecordIssuer issuer, CancellationToken cancellationToken)
    {
        var record = await records.FindRecordAsync(work.RecordId, cancellationToken)
            ?? throw new InvalidOperationException("The action response is missing.");
        var definition = await definitions.FindDefinitionAsync(record.Record.DefinitionId, cancellationToken);
        if (definition?.StandardKey != issuer.StandardKey || definition.StandardBindingsJson is not { } bindings) return null;
        var bound = FormDraftPolicy.Read<Dictionary<string, Guid>>(bindings);
        return new(work.RunId, work.RecordId, FormIssueDataPolicy.Read(record.Record, bound, definition.OriginalCulture));
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
