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
/// and only while the run waits for it; a repeated or stale decision changes nothing. «No emitir» from a channel hands back what
/// the channel answers, in the language «¿La emito?» was asked in.
/// </summary>
internal sealed class FormIssueConfirmationService(FormRecordAmendmentGuard access, IFormActionRunRepository runs, IFormDraftRepository drafts,
    IFormActionRecipientReader recipients, FormWorkflowReadPolicy workflow, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    ILogger<FormIssueConfirmationService> logger) : IFormIssueConfirmationService
{
    public Task<Result<FormWorkflowResponse>> ConfirmAsync(Guid recordId, Guid runId, bool issue, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger, "ConfirmFormIssue", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            var decided = await unitOfWork.ExecuteInTransactionAsync(ct => DecideAsync(recordId, runId, actor.Value.Actor, issue, ct), CommitPolicy.OnSuccess, cancellationToken);
            return decided.IsFailure ? decided.Error : await workflow.ReadAsync(recordId, cancellationToken);
        });

    public Task<Result<string?>> ReplyAsync(Guid runId, Guid actorUserId, bool issue, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<string?>>(logger, "ReplyFormIssue", async () =>
        {
            var decided = await unitOfWork.ExecuteInTransactionAsync(ct => DecideAsync(null, runId, actorUserId, issue, ct), CommitPolicy.OnSuccess, cancellationToken);
            if (decided.IsFailure) return decided.Error;
            if (decided.Value is not { } voided) return Result.Success<string?>(null);
            var culture = (await recipients.FindAsync(actorUserId, cancellationToken))?.Culture is "en" ? "en" : "es";
            return FormIssueConfirmationPolicy.Voided(voided.Prefix, voided.Number, culture);
        });

    // After «No emitir», the prefix and number of the voided response; nothing after «Emitir».
    private async Task<Result<(string Prefix, long Number)?>> DecideAsync(Guid? recordId, Guid runId, Guid actor, bool issue, CancellationToken cancellationToken)
    {
        await drafts.LockDefinitionsAsync(cancellationToken);
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null || recordId is { } id && run.RecordId != id || run.Kind != "IssueDocument") return FormActionErrors.NotFound;
        var record = await runs.GetRecordAsync(run.RecordId, cancellationToken) ?? throw new InvalidOperationException("The action response is missing.");
        if (record.ActorUserId != actor) return FormDefinitionErrors.Forbidden;
        if (run.State != FormActionRunState.Uncertain || run.FailureCode != FormActionErrors.AwaitingConfirmation.Code) return FormActionErrors.StateConflict;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (issue)
        {
            var requested = run.RequestIssueReview(run.Fence, true, actor, now);
            if (requested.IsFailure) return requested.Error;
            return Result.Success<(string Prefix, long Number)?>(null);
        }
        var voided = record.Void(actor, FormIssueConfirmationPolicy.NotIssued(record.PresentationCulture ?? "es"), now);
        if (voided.IsFailure) return voided.Error;
        if (await runs.GetOpenTaskAsync(record.Id, cancellationToken) is { } task && task.Close() is { IsFailure: true } closed) return closed.Error;
        var consulted = run.RequestConsult(now);
        if (consulted.IsFailure) return consulted.Error;
        var definition = await drafts.GetAsync(record.DefinitionId, cancellationToken) ?? throw new InvalidOperationException("The response form is missing.");
        return Result.Success<(string Prefix, long Number)?>((definition.Prefix, record.Number));
    }
}
