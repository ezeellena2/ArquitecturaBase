using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAudioDispatchService(IWhatsAppAudioReader reader,IWhatsAppAudioRepository messages,
    IWhatsAppConversationRepository conversations,WhatsAppAudioPreparationPolicy preparation,
    WhatsAppAudioAccessPolicy access,IUnitOfWork unitOfWork,WhatsAppAudioPresentationPolicy presentation,
    ILogger<WhatsAppAudioDispatchService> logger) : IWhatsAppAudioDispatchService
{
    public Task<Result<WhatsAppAssistantWorkResponse?>> PrepareAsync(Guid contactId,CancellationToken ct)=>
        OperationLog.RunAsync<Result<WhatsAppAssistantWorkResponse?>>(logger,"PrepareWhatsAppAudio",()=>PrepareCoreAsync(contactId,ct));
    private async Task<Result<WhatsAppAssistantWorkResponse?>> PrepareCoreAsync(Guid contactId,CancellationToken ct)
    {
        var source=await reader.FindPendingAsync(contactId,presentation.NowUtc,ct);
        if(source is null || presentation.IsExpired(source))return Result.Success<WhatsAppAssistantWorkResponse?>(null);
        var before=CopyOnly(source)?null:source.RequestId is null?await access.CaptureAsync(source,null,false,ct):await access.RevalidateAsync(source,ct);
        // Authentication, provider reads and private WriteOnce happen outside the WA transaction.
        var acquired=source.RequestId is not null && before?.IsFailure==true?null:await preparation.AcquireAsync(source,ct);
        return await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var contact=await conversations.GetForUpdateAsync(contactId,token);
            if(contact is null)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            var message=await messages.GetForUpdateAsync(contactId,source.MessageId,token);
            var current=await reader.FindAsync(contactId,source.MessageId,token);
            if(message is null || current is null || current.State is not (WhatsAppAudioState.Pending or WhatsAppAudioState.Transcribing))return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            if(presentation.IsExpired(current)){message.SetAudioState(WhatsAppAudioState.Expired);return Result.Success<WhatsAppAssistantWorkResponse?>(null);}
            var copyOnly=CopyOnly(current);
            var captured=copyOnly?null:current.RequestId is null?await access.CaptureAsync(current,null,false,token):await access.RevalidateAsync(current,token);
            if(current.RequestId is not null && captured?.IsFailure==true)
            {
                message.SetAudioState(WhatsAppAudioState.Unavailable,captured.Error.Code);
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            }
            if(acquired is null)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            if(acquired.IsFailure)
            {
                if(acquired.Error==FileErrors.Unavailable)message.RegisterAudioFailure(presentation.NowUtc,acquired.Error.Code);
                else message.SetAudioState(WhatsAppAudioState.Unavailable,acquired.Error.Code);
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            }
            if(message.StoreAudio(acquired.Value.Key,acquired.Value.Size).IsFailure)return Result.Failure<WhatsAppAssistantWorkResponse?>(WhatsAppChatErrors.RequestConflict);
            if(captured?.IsFailure==true)
            {
                message.SetAudioState(WhatsAppAudioState.Unavailable,captured.Error.Code);
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            }
            if(copyOnly)return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            if(acquired.Value.Size>WhatsAppAudioStoragePolicy.MaximumNormalizationBytes)
            {
                message.SetAudioState(WhatsAppAudioState.Unavailable,FileErrors.TooLarge.Code);
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            }
            var hasCapacity = await access.HasOfferCapacityAsync(contact.Id, captured!.Value, token);
            if (!hasCapacity && current.RequestId is null)
            {
                message.SetAudioState(WhatsAppAudioState.Unavailable, WhatsAppAssistantErrors.Unavailable.Code);
                return Result.Success<WhatsAppAssistantWorkResponse?>(null);
            }
            var request=message.RequestAudioNormalization(captured.Value.ActorUserId,JsonSerializer.Serialize(captured.Value,JsonSerializerOptions.Web),
                contact.LinkRevision,presentation.ExpiresAtUtc(current),presentation.NowUtc);
            if(request.IsFailure)return Result.Failure<WhatsAppAssistantWorkResponse?>(request.Error);
            message.SetAudioState(WhatsAppAudioState.Transcribing);
            var expires=message.AudioContentExpiresAtUtc!.Value;
            var operation=new AssistantOperationRequest(request.Value,captured.Value,"",message.OccurredAtUtc,expires)
            {Audio=new(message.AudioStorageKey!,$"audio_{message.Id:N}.ogg",message.AudioContentType!,message.AudioSize!.Value,message.AudioSha256!,expires)};
            return Result.Success<WhatsAppAssistantWorkResponse?>(new(request.Value,operation) { RecoverOnly = !hasCapacity });
        },CommitPolicy.OnSuccess,ct);
    }
    private static bool CopyOnly(WhatsAppAudioSourceRow source)=>source.RequestId is null && source.Mode==WhatsAppConversationMode.Manual;
    public Task<Result> CompleteAsync(Guid contactId,Guid requestId,Result<AssistantOperationResponse> outcome,CancellationToken ct)=>
        OperationLog.RunAsync<Result>(logger,"CompleteWhatsAppAudio",()=>unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            if(await conversations.GetForUpdateAsync(contactId,token) is null)return Result.Success();
            var message=await messages.GetByRequestForUpdateAsync(contactId,requestId,token);
            if(message is null)return Result.Success();
            var source=await reader.FindAsync(contactId,message.Id,token);
            if(source is null)return Result.Success();
            if(presentation.IsExpired(source)){message.SetAudioState(WhatsAppAudioState.Expired);return Result.Success();}
            var authorized=await access.RevalidateAsync(source,token);
            if(authorized.IsFailure){message.SetAudioState(WhatsAppAudioState.Unavailable,authorized.Error.Code);return Result.Success();}
            if(outcome.IsFailure)
            {
                if (outcome.Error.Metadata?.TryGetValue("retryable", out var retryable) == true && retryable is true)
                    message.SetAudioState(WhatsAppAudioState.Transcribing);
                else message.SetAudioState(WhatsAppAudioState.Unavailable,outcome.Error.Code);
                return Result.Success();
            }
            if(outcome.Value.RequestId!=requestId)return Result.Failure(WhatsAppChatErrors.RequestConflict);
            switch(outcome.Value.State)
            {
                case "completed":
                    if(outcome.Value.NormalizedAudio is not {} normalized){message.SetAudioState(WhatsAppAudioState.Failed);return Result.Success();}
                    return message.CompleteAudio(requestId,normalized.Text,presentation.NowUtc);
                case "pending": case "running": case "queued":
                    message.SetAudioState(WhatsAppAudioState.Transcribing);break;
                case "uncertain":message.SetAudioState(WhatsAppAudioState.Uncertain,outcome.Value.FailureCode);break;
                case "expired":message.SetAudioState(WhatsAppAudioState.Expired);break;
                default:message.SetAudioState(WhatsAppAudioState.Failed,outcome.Value.FailureCode);break;
            }
            return Result.Success();
        },CommitPolicy.OnSuccess,ct));
}
