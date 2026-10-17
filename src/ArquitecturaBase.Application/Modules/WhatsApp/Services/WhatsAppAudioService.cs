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
internal sealed class WhatsAppAudioService(WhatsAppAudioAccessPolicy access,IWhatsAppAudioReader reader,IWhatsAppAudioRepository messages,
    IWhatsAppConversationRepository conversations,IUnitOfWork unitOfWork,WhatsAppAudioPresentationPolicy presentation,IWhatsAppInboundSignal signal,
    ILogger<WhatsAppAudioService> logger):IWhatsAppAudioService
{
    public Task<Result<WhatsAppAudioResponse>> RequestAsync(Guid contactId,Guid messageId,CancellationToken ct)=>
        OperationLog.RunAsync<Result<WhatsAppAudioResponse>>(logger,"RequestWhatsAppAudio",()=>RequestCoreAsync(contactId,messageId,ct));
    private async Task<Result<WhatsAppAudioResponse>> RequestCoreAsync(Guid contactId,Guid messageId,CancellationToken ct)
    {
        var actor=await access.RequireOperatorAsync(ct);if(actor.IsFailure)return actor.Error;
        var result=await unitOfWork.ExecuteInTransactionAsync(async token=>
        {
            var contact=await conversations.GetForUpdateAsync(contactId,token);
            if(contact is null)return Result.Failure<WhatsAppAudioResponse>(WhatsAppChatErrors.NotFound);
            var message=await messages.GetForUpdateAsync(contactId,messageId,token);
            var source=await reader.FindAsync(contactId,messageId,token);
            if(message is null || source is null)return Result.Failure<WhatsAppAudioResponse>(WhatsAppChatErrors.NotFound);
            if(presentation.IsExpired(source)){message.SetAudioState(WhatsAppAudioState.Expired);return Result.Success(presentation.View(source));}
            if(source.RequestId is not null || source.State!=WhatsAppAudioState.Pending)return Result.Success(presentation.View(source));
            var context=await access.CaptureAsync(source,actor.Value,false,token);
            if(context.IsFailure)return Result.Failure<WhatsAppAudioResponse>(context.Error);
            var accepted=message.RequestAudioNormalization(actor.Value,JsonSerializer.Serialize(context.Value,JsonSerializerOptions.Web),contact.LinkRevision,
                presentation.ExpiresAtUtc(source),presentation.NowUtc);
            return accepted.IsFailure?Result.Failure<WhatsAppAudioResponse>(accepted.Error):Result.Success(presentation.View(source));
        },CommitPolicy.OnSuccess,ct);
        if(result.IsSuccess)signal.Notify();
        return result;
    }
}
