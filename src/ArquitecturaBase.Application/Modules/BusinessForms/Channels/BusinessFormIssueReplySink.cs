using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>
/// A button of «¿La emito?» pressed in a channel: «Emitir», «No emitir» or, after a summary that failed, «Volver a probar», in the
/// name of the person who pressed it. A message after «Volver a probar» has an id of its own, and its buttons name the run.
/// «No emitir» hands back the text the channel answers; the others none, because what follows comes from the run and the rules.
/// </summary>
internal sealed class BusinessFormIssueReplySink(IFormIssueConfirmationService confirmations) : IStructuredRecordActionReplySink
{
    public async Task<StructuredRecordActionReply> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, CancellationToken cancellationToken)
    {
        if (!FormIssueConfirmationPolicy.TryReadChoice(runId, choiceId, out var run, out var choice)) return new(StructuredRecordActionReplyState.Ignored);
        var replied = await confirmations.ReplyAsync(run, recipientUserId, choice != FormIssueConfirmationPolicy.Skip,
            choice == FormIssueConfirmationPolicy.Retry, cancellationToken);
        return replied.IsSuccess ? new(StructuredRecordActionReplyState.Applied, replied.Value) : new(StructuredRecordActionReplyState.Ignored);
    }
}
