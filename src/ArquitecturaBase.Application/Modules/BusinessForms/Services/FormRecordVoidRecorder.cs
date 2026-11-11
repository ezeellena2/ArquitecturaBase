using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The one way to void a response, in the transaction of whoever voids it (the app, «No emitir», a reversal that ends, a review
/// that does not issue): the response with its reason (a text, or the code of a reason the system decided), its open approval
/// task, and the issuances that waited for whoever loaded it («¿La emito?»), released so they end «No aplica». A response already
/// voided keeps its first void: voiding it again changes nothing and does not fail, so two ways of voiding that cross (the app
/// and «No emitir») never leave a run retrying forever. It does not save on its own.
/// </summary>
internal sealed class FormRecordVoidRecorder(IFormActionRunRepository runs)
{
    public async Task<Result> VoidAsync(BusinessFormRecord record, Guid actorUserId, string? reason, string? reasonCode, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (record.State != FormRecordState.Voided)
        {
            var voided = reasonCode is null ? record.Void(actorUserId, reason, nowUtc) : record.VoidFor(actorUserId, reasonCode, nowUtc);
            if (voided.IsFailure) return voided.Error;
            if (await runs.GetOpenTaskAsync(record.Id, cancellationToken) is { } task && task.Close() is { IsFailure: true } closed) return closed.Error;
        }
        foreach (var run in await runs.ListLiveIssuancesAsync(record.Id, cancellationToken))
            if (run is { State: FormActionRunState.Uncertain } && run.FailureCode == FormActionErrors.AwaitingConfirmation.Code
                && run.RequestConsult(nowUtc) is { IsFailure: true } released)
                return released.Error;
        return Result.Success();
    }
}
