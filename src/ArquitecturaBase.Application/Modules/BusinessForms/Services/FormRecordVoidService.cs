using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Voids a current response under the definitions lock, the one approvals take: a decision cannot cross it. What already
/// happened stays (notices, PDF); only the open approval task closes. It is internal configuration of a response, so a
/// changed response answers 409 and nothing is retried. A response with an issuance is not voided here (NC1-A): each issuance
/// gets a reversal run that the worker carries outside any transaction, and the response is voided when it ends. That takes,
/// besides voiding, the permission of the issuer.
/// </summary>
internal sealed class FormRecordVoidService(FormRecordAmendmentGuard access, IFormRecordRepository records, IUnitOfWork unitOfWork,
    IRequestValidator validator, TimeProvider timeProvider, ILogger<FormRecordVoidService> logger, IFormActionRunRepository runs,
    IEnumerable<ArquitecturaBase.Application.Interfaces.Channels.IStructuredRecordIssuer> issuers) : IFormRecordVoidService
{
    public Task<Result> VoidAsync(Guid definitionId, Guid recordId, VoidFormRecordRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "VoidFormRecord", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
            {
                await records.LockDefinitionsAsync(ct);
                if (!await access.StillAllowedAsync(actor.Value, ct)) return FormDefinitionErrors.Forbidden;
                var record = await records.GetRecordAsync(recordId, ct);
                if (record is null || record.DefinitionId != definitionId) return FormRecordErrors.NotFound;
                var allowed = FormRecordAmendmentGuard.Allows(actor.Value, record.ActorUserId, record.State, record.ReplacedByRecordId is not null);
                if (allowed.IsFailure) return allowed.Error;
                var reversed = await ReverseAsync(record, actor.Value.Actor, request.Reason, ct);
                if (reversed is not null) return reversed;
                var voided = record.Void(actor.Value.Actor, request.Reason, timeProvider.GetUtcNow().UtcDateTime);
                if (voided.IsFailure) return voided.Error;
                if (await records.GetOpenTaskAsync(record.Id, ct) is { } task && task.Close() is { IsFailure: true } closed) return closed.Error;
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    // Null when the response has no live issuance and voids now; otherwise the reversal runs it started, or why not.
    private async Task<Result?> ReverseAsync(BusinessFormRecord record, Guid actor, string? reason, CancellationToken cancellationToken)
    {
        var issued = new List<BusinessFormActionRun>();
        foreach (var eventName in new[] { "Received", "Approved" })
            issued.AddRange((await runs.ListForEventAsync(record.Id, eventName, cancellationToken)).Where(run => run.Kind == "IssueDocument"
                && run.State is not (FormActionRunState.Failed or FormActionRunState.NotApplicable)));
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
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Each attempt is its own run, so a reversal the provider rejected leaves room for another with a new key.
        foreach (var run in issued)
        {
            var issuerId = FormDraftPolicy.Read<FormActionSnapshot>(run.SnapshotJson).IssuerId;
            var created = BusinessFormActionRun.Create(record.DefinitionId, record.Id, run.RuleSetId, run.RuleId, Guid.CreateVersion7(), "Voided",
                "IssueDocument", 0, null, "", true, now);
            created.SealSnapshot(FormDraftPolicy.Serialize(new FormActionSnapshot(null, null, null, [], null, issuerId, run.Id, reason, actor)));
            runs.Add(created);
        }
        return Result.Success();
    }
}
