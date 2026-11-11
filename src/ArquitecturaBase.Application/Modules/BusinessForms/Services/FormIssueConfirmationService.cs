using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The decision goes under the definitions lock, the one voiding and approvals take. Only the author of the response decides,
/// and only while the run waits for it; a repeated or stale decision changes nothing.
/// </summary>
internal sealed class FormIssueConfirmationService(FormRecordAmendmentGuard access, IFormActionRunRepository runs, IFormDraftRepository drafts,
    FormWorkflowReadPolicy workflow, IUnitOfWork unitOfWork, TimeProvider timeProvider, ILogger<FormIssueConfirmationService> logger)
    : IFormIssueConfirmationService
{
    public Task<Result<FormWorkflowResponse>> ConfirmAsync(Guid recordId, Guid runId, bool issue, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger, "ConfirmFormIssue", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            var decided = await unitOfWork.ExecuteInTransactionAsync(ct => DecideAsync(recordId, runId, actor.Value.Actor, issue, ct), CommitPolicy.OnSuccess, cancellationToken);
            return decided.IsFailure ? decided.Error : await workflow.ReadAsync(recordId, cancellationToken);
        });

    public Task<Result> ReplyAsync(Guid runId, Guid actorUserId, bool issue, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "ReplyFormIssue", () =>
            unitOfWork.ExecuteInTransactionAsync(ct => DecideAsync(null, runId, actorUserId, issue, ct), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result> DecideAsync(Guid? recordId, Guid runId, Guid actor, bool issue, CancellationToken cancellationToken)
    {
        await drafts.LockDefinitionsAsync(cancellationToken);
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null || recordId is { } id && run.RecordId != id || run.Kind != "IssueDocument") return FormActionErrors.NotFound;
        var record = await runs.GetRecordAsync(run.RecordId, cancellationToken) ?? throw new InvalidOperationException("The action response is missing.");
        if (record.ActorUserId != actor) return FormDefinitionErrors.Forbidden;
        if (run.State != FormActionRunState.Uncertain || run.FailureCode != FormActionErrors.AwaitingConfirmation.Code) return FormActionErrors.StateConflict;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (issue) return run.RequestIssueReview(run.Fence, true, actor, now);
        var voided = record.Void(actor, FormIssueConfirmationPolicy.NotIssued(record.PresentationCulture ?? "es"), now);
        if (voided.IsFailure) return voided.Error;
        if (await runs.GetOpenTaskAsync(record.Id, cancellationToken) is { } task && task.Close() is { IsFailure: true } closed) return closed.Error;
        return run.RequestConsult(now);
    }
}
