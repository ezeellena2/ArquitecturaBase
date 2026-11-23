using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;
// The audio copies of the messages that keep their content, with the retention of Configuración › WhatsApp read now (RC3-A).
internal sealed class WhatsAppAudioRetentionSource(IWhatsAppAudioReader reader,IWhatsAppSettingsReader settings,TimeProvider clock):IPrivateFileRetentionSource
{
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> keys,CancellationToken ct)=>
        await reader.ListReferencedKeysAsync(keys,clock.GetUtcNow().UtcDateTime.AddDays(-(await settings.FindAsync(ct)).MessageRetentionDays),ct);
}
