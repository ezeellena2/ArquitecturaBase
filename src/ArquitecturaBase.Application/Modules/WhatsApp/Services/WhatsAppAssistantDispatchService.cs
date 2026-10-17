using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantDispatchService(WhatsAppAssistantAccessPolicy access,WhatsAppAssistantSuggestionPolicy proposals,
    IWhatsAppAssistantReader reader,IWhatsAppAssistantRepository requests,IWhatsAppConversationRepository conversations,
    IUnitOfWork unitOfWork,TimeProvider clock,ILogger<WhatsAppAssistantDispatchService> logger) : IWhatsAppAssistantDispatchService
{
    public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid contactId,CancellationToken ct) =>
        OperationLog.RunAsync<Result<WhatsAppAssistantWorkResponse?>>(logger,"PrepareWhatsAppAssistant",()=>unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var contact=await conversations.GetForUpdateAsync(contactId,token);
            var row=await requests.GetPendingAsync(contactId,token);
            if(contact is null || row is null || row.Kind!=WhatsAppAssistantRequestKind.Manual) return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            var reference=WhatsAppAssistantSuggestionPolicy.Reference(row);
            if(await access.RevalidateAsync(contact,reference,token) is not null || row.ContentExpiresAtUtc<=clock.GetUtcNow().UtcDateTime
                || await reader.FindLatestSourceAsync(contactId,token) is not {} latest || latest.Id!=row.SourceMessageId)
            {row.FinishWithoutReply(true,clock.GetUtcNow().UtcDateTime);return Result.Success<WhatsAppAssistantWorkResponse?>(null);}
            return Result.Success<WhatsAppAssistantWorkResponse?>(new(row.RequestId,
                new(row.RequestId,WhatsAppAssistantSuggestionPolicy.Context(reference),latest.Body,latest.OccurredAtUtc,row.ContentExpiresAtUtc)));
        },CommitPolicy.OnSuccess,ct));

    public Task<Result> CompleteAsync(Guid contactId,Guid requestId,Result<AssistantOperationResponse> outcome,CancellationToken ct) =>
        OperationLog.RunAsync<Result>(logger,"CompleteWhatsAppAssistant",()=>CompleteCoreAsync(contactId,requestId,outcome,ct));

    private async Task<Result> CompleteCoreAsync(Guid contactId,Guid requestId,Result<AssistantOperationResponse> outcome,CancellationToken ct)
    {
        var original=await reader.FindAsync(contactId,requestId,ct);
        if(original is null) return WhatsAppChatErrors.NotFound;
        if(original.State!=WhatsAppAssistantRequestState.Pending) return Result.Success();
        if(outcome.IsSuccess && outcome.Value.RequestId!=requestId) return WhatsAppAssistantErrors.InvalidProposal;
        // The owner validates its proposal outside our transaction. No model read or effect is called under this lock.
        var observed=await proposals.ObserveAsync(original,outcome,ct);
        return await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var contact=await conversations.GetForUpdateAsync(contactId,token);
            var row=await requests.GetByRequestAsync(requestId,token);
            if(contact is null || row?.ContactId!=contactId) return WhatsAppChatErrors.NotFound;
            if(row.State!=WhatsAppAssistantRequestState.Pending) return Result.Success();
            if(await access.RevalidateAsync(contact,WhatsAppAssistantSuggestionPolicy.Reference(row),token) is not null
                || row.ContentExpiresAtUtc<=clock.GetUtcNow().UtcDateTime
                || await reader.FindLatestSourceAsync(contactId,token) is not {} latest || latest.Id!=row.SourceMessageId
                || observed.IsFailure)
            {row.FinishWithoutReply(true,clock.GetUtcNow().UtcDateTime);return Result.Success();}
            var view=observed.Value;
            if(view.Response.State==WhatsAppAssistantRequestState.Ready && view.Operation?.Proposal is {} proposal && view.Prefill is {} prefill)
                return row.Ready(proposal.Id,prefill.Form.DefinitionId,clock.GetUtcNow().UtcDateTime) ? Result.Success() : WhatsAppAssistantErrors.StateConflict;
            if(view.Response.State is WhatsAppAssistantRequestState.NoReply or WhatsAppAssistantRequestState.Blocked)
                row.FinishWithoutReply(view.Response.State==WhatsAppAssistantRequestState.Blocked,clock.GetUtcNow().UtcDateTime);
            return Result.Success();
        },CommitPolicy.OnSuccess,ct);
    }
}
