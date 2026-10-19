using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAudioPresentationPolicy(WhatsAppContentRetention retention,TimeProvider clock)
{
    public DateTime NowUtc => clock.GetUtcNow().UtcDateTime;
    public DateTime ExpiresAtUtc(WhatsAppAudioSourceRow source)
    {
        var maximum=source.OccurredAtUtc.AddDays(retention.MessageRetentionDays);
        return source.ContentExpiresAtUtc is {} captured && captured<maximum?captured:maximum;
    }
    public bool IsExpired(WhatsAppAudioSourceRow source)=>ExpiresAtUtc(source)<=NowUtc;
    public WhatsAppAudioResponse View(WhatsAppAudioSourceRow source)=>new(IsExpired(source)?WhatsAppAudioState.Expired:source.State,
        !IsExpired(source) && source.StorageKey is not null, IsExpired(source) ? null : Reason(source.State, source.FailureCode));
    public WhatsAppAudioResponse? View(WhatsAppMessageRow message)
    {
        if(message.Kind!=WhatsAppMessageKind.Audio)return null;
        var expires=message.OccurredAtUtc.AddDays(retention.MessageRetentionDays);
        if(message.Audio?.ContentExpiresAtUtc is {} captured && captured<expires)expires=captured;
        return expires<=NowUtc?new(WhatsAppAudioState.Expired,false):new(message.Audio?.State??WhatsAppAudioState.Unavailable,message.Audio?.HasContent??false,
            Reason(message.Audio?.State??WhatsAppAudioState.Unavailable,message.Audio?.FailureCode));
    }
    private static string? Reason(WhatsAppAudioState state, string? failureCode) =>
        state == WhatsAppAudioState.Unavailable && failureCode == FileErrors.TooLarge.Code ? "tooLarge" : null;

    public string? VisibleText(WhatsAppMessageRow message)=>message.Kind!=WhatsAppMessageKind.Audio?message.Body:
        View(message)?.State==WhatsAppAudioState.Completed?message.Body:null;
}
