using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppAssistantService(WhatsAppAssistantAccessPolicy access,WhatsAppAssistantSuggestionPolicy policy,
    IWhatsAppAssistantRepository requests,IWhatsAppConversationRepository conversations,IUnitOfWork unitOfWork,
    IWhatsAppInboundSignal signal,WhatsAppAssistantAcceptancePolicy acceptance,ILogger<WhatsAppAssistantService> logger) : IWhatsAppAssistantService
{
    public Task<Result<WhatsAppAssistantSuggestionResponse>> RequestAsync(RequestWhatsAppAssistantSuggestionRequest request,CancellationToken ct)=>
        OperationLog.RunAsync<Result<WhatsAppAssistantSuggestionResponse>>(logger,"RequestWhatsAppAssistantSuggestion",()=>RequestCoreAsync(request,ct));

    private async Task<Result<WhatsAppAssistantSuggestionResponse>> RequestCoreAsync(RequestWhatsAppAssistantSuggestionRequest request,CancellationToken ct)
    {
        var actor=await access.RequireOperatorAsync(ct);if(actor.IsFailure)return actor.Error;
        if(request.ContactId==Guid.Empty || request.RequestId==Guid.Empty || request.SourceMessageId==Guid.Empty || request.ExpectedModeRevision<0) return WhatsAppAssistantErrors.InvalidRequest;
        var availability=await policy.GetAvailabilityAsync(ct);
        var accepted=await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var contact=await conversations.GetForUpdateAsync(request.ContactId,token);
            if(contact is null)return Result.Failure<WhatsAppAssistantReferenceRow>(WhatsAppChatErrors.NotFound);
            if(contact.Mode!=WhatsAppConversationMode.Manual || contact.ModeRevision!=request.ExpectedModeRevision) return Result.Failure<WhatsAppAssistantReferenceRow>(WhatsAppAssistantErrors.StateConflict);
            var source=await policy.FindLatestSourceAsync(contact.Id,token);
            if(source is null || source.Id!=request.SourceMessageId || source.ContentExpiresAtUtc<=policy.NowUtc) return Result.Failure<WhatsAppAssistantReferenceRow>(WhatsAppAssistantErrors.StateConflict);
            var existing=await requests.GetBySourceAsync(contact.Id,source.Id,WhatsAppAssistantRequestKind.Manual,token);
            if(existing is not null)
            {
                var reference=WhatsAppAssistantSuggestionPolicy.Reference(existing);
                return await access.RevalidateAsync(contact,reference,token) is {} replayError?Result.Failure<WhatsAppAssistantReferenceRow>(replayError):Result.Success(reference);
            }
            if(availability.State!="available")return Result.Failure<WhatsAppAssistantReferenceRow>(WhatsAppAssistantErrors.Unavailable);
            if(await requests.GetByRequestAsync(request.RequestId,token) is not null)return Result.Failure<WhatsAppAssistantReferenceRow>(WhatsAppAssistantErrors.StateConflict);
            var captured=await access.CaptureAsync(contact,actor.Value.Id,token);
            if(captured.IsFailure)return Result.Failure<WhatsAppAssistantReferenceRow>(captured.Error);
            var row=WhatsAppAssistantRequest.Create(request.RequestId,contact.Id,source.Id,captured.Value.Context.ActorUserId,captured.Value.Context.SubjectUserId!.Value,
                WhatsAppAssistantRequestKind.Manual,contact.ModeRevision,contact.LinkRevision,JsonSerializer.Serialize(captured.Value.Context,JsonSerializerOptions.Web),policy.NowUtc,source.ContentExpiresAtUtc);
            requests.Add(row);return Result.Success(WhatsAppAssistantSuggestionPolicy.Reference(row));
        },CommitPolicy.OnSuccess,ct);
        if(accepted.IsFailure)return accepted.Error;
        signal.Notify();
        var view=await policy.ReadAsync(accepted.Value,ct,actor.Value.Id);
        return view.IsFailure?view.Error:view.Value.Response;
    }

    public Task<Result<WhatsAppAssistantSuggestionResponse>> GetAsync(Guid contactId,Guid requestId,CancellationToken ct)=>
        OperationLog.RunAsync<Result<WhatsAppAssistantSuggestionResponse>>(logger,"GetWhatsAppAssistantSuggestion",async()=>
        {
            var actor=await access.RequireOperatorAsync(ct);if(actor.IsFailure)return actor.Error;
            var reference=await policy.FindAsync(contactId,requestId,ct);
            if(reference is null || reference.Kind!=WhatsAppAssistantRequestKind.Manual)return WhatsAppChatErrors.NotFound;
            var view=await policy.ReadAsync(reference,ct,actor.Value.Id);
            return view.IsFailure?view.Error:view.Value.Response;
        });

    public Task<Result<WhatsAppAssistantSuggestionResponse>> DiscardAsync(Guid contactId,Guid requestId,CancellationToken ct)=>
        OperationLog.RunAsync<Result<WhatsAppAssistantSuggestionResponse>>(logger,"DiscardWhatsAppAssistantSuggestion",async()=>
        {
            var actor=await access.RequireOperatorAsync(ct);if(actor.IsFailure)return actor.Error;
            var reference=await policy.FindAsync(contactId,requestId,ct);
            if(reference is null || reference.Kind!=WhatsAppAssistantRequestKind.Manual)return WhatsAppChatErrors.NotFound;
            var view=await policy.ReadAsync(reference,ct,actor.Value.Id);if(view.IsFailure)return view.Error;
            return await unitOfWork.ExecuteInTransactionAsync(async token=>
            {
                var contact=await conversations.GetForUpdateAsync(contactId,token);
                if(contact is null)return Result.Failure<WhatsAppAssistantSuggestionResponse>(WhatsAppChatErrors.NotFound);
                var row=await requests.GetByRequestAsync(requestId,token);
                if(row is null || row.ContactId!=contactId || row.Kind!=WhatsAppAssistantRequestKind.Manual)return Result.Failure<WhatsAppAssistantSuggestionResponse>(WhatsAppChatErrors.NotFound);
                if(row.State==WhatsAppAssistantRequestState.Discarded)return Result.Success(new WhatsAppAssistantSuggestionResponse(row.RequestId,row.SourceMessageId,row.State,null,null));
                if(await access.RevalidateAsync(contact,WhatsAppAssistantSuggestionPolicy.Reference(row),token) is {} error)return Result.Failure<WhatsAppAssistantSuggestionResponse>(error);
                if(view.Value.Response.State==WhatsAppAssistantRequestState.Ready && view.Value.Operation?.Proposal is {} proposal)
                    row.Ready(proposal.Id,view.Value.Prefill?.Form.DefinitionId,policy.NowUtc);
                return row.Discard(actor.Value.Id,policy.NowUtc)?Result.Success(new WhatsAppAssistantSuggestionResponse(row.RequestId,row.SourceMessageId,row.State,null,null))
                    :Result.Failure<WhatsAppAssistantSuggestionResponse>(WhatsAppAssistantErrors.StateConflict);
            },CommitPolicy.OnSuccess,ct);
        });

    public Task<Result<WhatsAppOutgoingMessageResponse>> SendAsync(SendWhatsAppAssistantSuggestionRequest request,CancellationToken ct)=>
        OperationLog.RunAsync<Result<WhatsAppOutgoingMessageResponse>>(logger,"SendWhatsAppAssistantSuggestion",()=>SendCoreAsync(request,ct));

    private async Task<Result<WhatsAppOutgoingMessageResponse>> SendCoreAsync(SendWhatsAppAssistantSuggestionRequest request,CancellationToken ct)
    {
        var actor=await access.RequireOperatorAsync(ct);if(actor.IsFailure)return actor.Error;
        if(request.ContactId==Guid.Empty || request.SuggestionRequestId==Guid.Empty || request.RequestId==Guid.Empty || request.IncludedFieldIds is null
            || request.IncludedFieldIds.Distinct().Count()!=request.IncludedFieldIds.Count) return WhatsAppAssistantErrors.InvalidRequest;
        var reference=await policy.FindAsync(request.ContactId,request.SuggestionRequestId,ct);
        if(reference is null || reference.Kind!=WhatsAppAssistantRequestKind.Manual)return WhatsAppChatErrors.NotFound;
        var view=await policy.ReadAsync(reference,ct,actor.Value.Id);if(view.IsFailure)return view.Error;
        if(view.Value.Response is {State:WhatsAppAssistantRequestState.Sent,Outgoing:{} sent})return sent;
        if(view.Value.Response.State!=WhatsAppAssistantRequestState.Ready || view.Value.Prefill is not {} preview)return WhatsAppAssistantErrors.StateConflict;
        if(request.IncludedFieldIds.Any(id=>!preview.Fields.Any(field=>field.FieldId==id)))return WhatsAppAssistantErrors.InvalidRequest;
        var resolved=await acceptance.ResolveAsync(request.ContactId,view.Value,actor.Value.Id,ct);if(resolved.IsFailure)return resolved.Error;
        return await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var contact=await conversations.GetForUpdateAsync(request.ContactId,token);
            if(contact is null)return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.NotFound);
            var row=await requests.GetByRequestAsync(request.SuggestionRequestId,token);
            if(row is null || row.ContactId!=contact.Id || row.Kind!=WhatsAppAssistantRequestKind.Manual)return Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppChatErrors.NotFound);
            if(row.State==WhatsAppAssistantRequestState.Sent)
            {
                var replay=await policy.ReadAsync(WhatsAppAssistantSuggestionPolicy.Reference(row),token,actor.Value.Id);
                return replay.IsSuccess && replay.Value.Response.Outgoing is {} outgoing ? Result.Success(outgoing)
                    :Result.Failure<WhatsAppOutgoingMessageResponse>(WhatsAppAssistantErrors.StateConflict);
            }
            var current=await access.RequireOperatorAsync(token);if(current.IsFailure)return Result.Failure<WhatsAppOutgoingMessageResponse>(current.Error);
            if(await access.RevalidateAsync(contact,WhatsAppAssistantSuggestionPolicy.Reference(row),token) is {} error)
                return Result.Failure<WhatsAppOutgoingMessageResponse>(error);
            return await acceptance.AcceptAsync(contact,row,request,view.Value,actor.Value.Id,resolved.Value,token);
        },CommitPolicy.OnSuccess,ct);
    }
}
