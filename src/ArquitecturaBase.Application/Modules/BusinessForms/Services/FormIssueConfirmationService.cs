using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «¿La emito?» (EM2-A): whoever loaded the response decides in one transaction of its own (<see cref="FormIssueDecisionRecorder"/>),
/// from the detail of the app, which reads where it stands afterwards, or from a button of a channel. The channel hands the button
/// over outside any transaction of its own (<see cref="IStructuredRecordActionReplySink"/>), so this service implements that port and
/// decides in its own, as the other sinks of the module do. A message after «Volver a probar» has an id of its own, and its buttons
/// name the run. «No emitir» from a channel hands back what the channel answers, in the language «¿La emito?» was asked in; the
/// others no text, because what follows comes from the run and the rules. A stale, foreign or unauthorized button is ignored.
/// </summary>
internal sealed class FormIssueConfirmationService(FormRecordAmendmentGuard access, FormIssueDecisionRecorder decisions, FormWorkflowReadPolicy workflow,
    IFormActionRecipientReader recipients, IUnitOfWork unitOfWork, ILogger<FormIssueConfirmationService> logger)
    : IFormIssueConfirmationService, IStructuredRecordActionReplySink
{
    private static readonly StructuredRecordActionReply Ignored = new(StructuredRecordActionReplyState.Ignored);

    public Task<Result<FormWorkflowResponse>> ConfirmAsync(Guid recordId, Guid runId, bool issue, bool retry, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormWorkflowResponse>>(logger, "ConfirmFormIssue", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            var decided = await unitOfWork.ExecuteInTransactionAsync(ct => decisions.DecideAsync(recordId, runId, actor.Value.Actor, issue, retry, ct),
                CommitPolicy.OnSuccess, cancellationToken);
            return decided.IsFailure ? decided.Error : await workflow.ReadAsync(recordId, cancellationToken);
        });

    public async Task<StructuredRecordActionReply> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, CancellationToken cancellationToken)
    {
        if (!FormIssueConfirmationPolicy.TryReadChoice(runId, choiceId, out var run, out var choice)) return Ignored;
        var replied = await OperationLog.RunAsync<Result<string?>>(logger, "ReplyFormIssue", async () =>
        {
            var decided = await unitOfWork.ExecuteInTransactionAsync(ct => decisions.DecideAsync(null, run, recipientUserId,
                choice != FormIssueConfirmationPolicy.Skip, choice == FormIssueConfirmationPolicy.Retry, ct), CommitPolicy.OnSuccess, cancellationToken);
            if (decided.IsFailure) return decided.Error;
            if (decided.Value is not { } voided) return Result.Success<string?>(null);
            var culture = (await recipients.FindAsync(recipientUserId, cancellationToken))?.Culture is "en" ? "en" : "es";
            return FormIssueConfirmationPolicy.Voided(voided.Prefix, voided.Number, culture);
        });
        return replied.IsSuccess ? new(StructuredRecordActionReplyState.Applied, replied.Value) : Ignored;
    }
}
