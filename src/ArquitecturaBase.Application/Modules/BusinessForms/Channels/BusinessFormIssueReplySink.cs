using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>A button of «¿La emito?» pressed in a channel: «Emitir» or «No emitir», in the name of the person who pressed it.</summary>
internal sealed class BusinessFormIssueReplySink(IFormIssueConfirmationService confirmations) : IStructuredRecordActionReplySink
{
    public async Task<StructuredRecordActionReplyState> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, CancellationToken cancellationToken)
    {
        if (choiceId is not (FormIssueConfirmationPolicy.Issue or FormIssueConfirmationPolicy.Skip)) return StructuredRecordActionReplyState.Ignored;
        var replied = await confirmations.ReplyAsync(runId, recipientUserId, choiceId == FormIssueConfirmationPolicy.Issue, cancellationToken);
        return replied.IsSuccess ? StructuredRecordActionReplyState.Applied : StructuredRecordActionReplyState.Ignored;
    }
}
