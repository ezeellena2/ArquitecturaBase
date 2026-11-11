using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// NC1-A: voiding a response with live issuances (<see cref="BusinessFormActionRun.LiveIssuance"/>) starts, in the transaction of
/// the void, a reversal run for each one, that the worker carries outside any transaction; the response is voided when they end.
/// It takes, besides voiding, the permission of each issuer. It does not save on its own.
/// </summary>
internal sealed class FormIssueReversalRecorder(IFormActionRunRepository runs, IEnumerable<IStructuredRecordIssuer> issuers, FormRecordAmendmentGuard access)
{
    /// <summary>Null when the response has no live issuance and voids now; otherwise the reversal runs it started, or why not.</summary>
    public async Task<Result?> ReverseAsync(BusinessFormRecord record, Guid actor, string? reason, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var issued = await runs.ListLiveIssuancesAsync(record.Id, cancellationToken);
        if (issued.Count == 0) return null;
        var reversals = await runs.ListForEventAsync(record.Id, "Voided", cancellationToken);
        if (reversals.Any(run => run.State is not (FormActionRunState.Failed or FormActionRunState.NotApplicable or FormActionRunState.Completed)))
            return FormRecordErrors.Voiding;
        foreach (var run in issued)
        {
            var issuerId = FormDraftPolicy.Read<FormActionSnapshot>(run.SnapshotJson).IssuerId;
            if (issuers.FirstOrDefault(issuer => issuer.IssuerId == issuerId) is not { } issuer) return FormActionErrors.IssuerUnavailable;
            if (!await access.HasPermissionAsync(actor, issuer.ReviewPermission, cancellationToken)) return FormDefinitionErrors.Forbidden;
        }
        // Each attempt is its own run, so a reversal the provider rejected leaves room for another with a new key.
        foreach (var run in issued)
        {
            var issuerId = FormDraftPolicy.Read<FormActionSnapshot>(run.SnapshotJson).IssuerId;
            var created = BusinessFormActionRun.Create(record.DefinitionId, record.Id, run.RuleSetId, run.RuleId, Guid.CreateVersion7(), "Voided",
                "IssueDocument", 0, null, "", true, nowUtc);
            created.SealSnapshot(FormDraftPolicy.Serialize(new FormActionSnapshot(null, null, null, [], null, issuerId, run.Id, reason, actor)));
            runs.Add(created);
        }
        return Result.Success();
    }
}
