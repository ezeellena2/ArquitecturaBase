using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAudioContentService(WhatsAppAudioAccessPolicy access,IWhatsAppAudioReader reader,WhatsAppAudioStoragePolicy storage,
    WhatsAppAudioPresentationPolicy presentation,ILogger<WhatsAppAudioContentService> logger):IWhatsAppAudioContentService
{
    public Task<Result<WhatsAppAudioContentResponse>> OpenAsync(Guid contactId,Guid messageId,CancellationToken ct)=>
        OperationLog.RunAsync<Result<WhatsAppAudioContentResponse>>(logger,"OpenWhatsAppAudio",async()=>
        {
            var actor=await access.RequireOperatorAsync(ct);if(actor.IsFailure)return actor.Error;
            var source=await reader.FindAsync(contactId,messageId,ct);
            if(source is null || presentation.IsExpired(source) || source.StorageKey!=WhatsAppAudioStoragePolicy.Key(messageId) || source.Sha256 is null)return WhatsAppChatErrors.NotFound;
            var opened=await storage.OpenAsync(messageId,source.Sha256,ct);if(opened.IsFailure)return opened.Error;
            var permitted=await access.RequireOperatorAsync(ct);
            var current=await reader.FindAsync(contactId,messageId,ct);
            if(permitted.IsFailure || current is null || presentation.IsExpired(current) || current.StorageKey!=source.StorageKey || current.Sha256!=source.Sha256)
            {await opened.Value.DisposeAsync();return permitted.IsFailure?permitted.Error:WhatsAppChatErrors.NotFound;}
            return new WhatsAppAudioContentResponse(opened.Value,"audio/ogg",$"audio_{messageId:N}.ogg");
        });
}
