using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAudioPreparationPolicy(IWhatsAppMediaClient media, WhatsAppAudioStoragePolicy storage)
{
    public async Task<Result<WhatsAppStoredAudio>> AcquireAsync(WhatsAppAudioSourceRow source,CancellationToken ct)
    {
        if(source.MediaId is null || source.ContentType is null || source.Sha256 is null)return FileErrors.InvalidMetadata;
        var found=await storage.FindAsync(source.MessageId,source.Sha256,ct);
        if(found.IsFailure)return found.Error;if(found.Value is {} value)return value;
        var downloaded=await media.DownloadAsync(source.MediaId,source.ContentType,source.Sha256,ct);
        return downloaded.IsFailure?downloaded.Error:await storage.SaveAsync(source.MessageId,downloaded.Value.Content,source.Sha256,ct);
    }
}
