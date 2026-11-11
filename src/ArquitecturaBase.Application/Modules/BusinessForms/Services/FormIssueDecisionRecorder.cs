using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «¿La emito?» (EM2-A): the decision of whoever loaded a response from a channel, from the detail of the app or from a button of
/// the channel, in the transaction of whoever decides and under the definitions lock, the one voiding and approvals take. Only the
/// author decides, with the permissions they have now (<see cref="FormRecordAmendmentGuard"/>): someone else's response is
/// forbidden for whoever reads responses and missing for whoever only reads their own. Only a response that still stands, while
/// its run waits for that person; a repeated or stale decision changes nothing. Going on is what the person saw: «Emitir» on a
/// summary that came out, «Volver a probar» on one that failed (decision 4); either releases the run, which issues or asks for
/// the summary again. «No emitir» voids the response with the code of «No se emitió». It does not save on its own.
/// </summary>
internal sealed class FormIssueDecisionRecorder(FormRecordAmendmentGuard access, IFormActionRunRepository runs, IFormDraftRepository drafts,
    FormRecordVoidRecorder voids, TimeProvider timeProvider)
{
    /// <summary>After «No emitir», the prefix and number of the voided response; nothing after «Emitir» or «Volver a probar».</summary>
    public async Task<Result<(string Prefix, long Number)?>> DecideAsync(Guid? recordId, Guid runId, Guid actor, bool issue, bool retry,
        CancellationToken cancellationToken)
    {
        await drafts.LockDefinitionsAsync(cancellationToken);
        if (await access.FindAsync(actor, cancellationToken) is not { } current) return FormDefinitionErrors.Forbidden;
        var run = await runs.GetAsync(runId, cancellationToken);
        if (run is null || recordId is { } id && run.RecordId != id || run.Kind != "IssueDocument") return FormActionErrors.NotFound;
        var record = await runs.GetRecordAsync(run.RecordId, cancellationToken) ?? throw new InvalidOperationException("The action response is missing.");
        if (record.ActorUserId != actor) return current.CanRead ? FormDefinitionErrors.Forbidden : FormRecordErrors.NotFound;
        if (!record.CanBeAmended || run.State != FormActionRunState.Uncertain || run.FailureCode != FormActionErrors.AwaitingConfirmation.Code)
            return FormActionErrors.StateConflict;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (issue)
        {
            var failed = run.OutcomeJson is not null && FormDraftPolicy.Read<FormActionOutcomeResponse>(run.OutcomeJson).IssuePreview?.FailureCode is not null;
            if (failed != retry) return FormActionErrors.StateConflict;
            var requested = run.RequestIssueReview(run.Fence, true, actor, now);
            return requested.IsFailure ? requested.Error : Result.Success<(string Prefix, long Number)?>(null);
        }
        // The run that waited is released with the void and ends «No aplica»: nothing goes to the issuer.
        var voided = await voids.VoidAsync(record, actor, null, FormActionErrors.NotIssued.Code, now, cancellationToken);
        if (voided.IsFailure) return voided.Error;
        var definition = await drafts.GetAsync(record.DefinitionId, cancellationToken) ?? throw new InvalidOperationException("The response form is missing.");
        return Result.Success<(string Prefix, long Number)?>((definition.Prefix, record.Number));
    }
}
