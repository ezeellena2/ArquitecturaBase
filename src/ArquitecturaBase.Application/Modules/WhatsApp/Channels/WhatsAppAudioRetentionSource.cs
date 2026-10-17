using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;
internal sealed class WhatsAppAudioRetentionSource(IWhatsAppAudioReader reader,WhatsAppContentRetention retention,TimeProvider clock):IPrivateFileRetentionSource
{
    public Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> keys,CancellationToken ct)=>
        reader.ListReferencedKeysAsync(keys,clock.GetUtcNow().UtcDateTime.AddDays(-retention.MessageRetentionDays),ct);
}
