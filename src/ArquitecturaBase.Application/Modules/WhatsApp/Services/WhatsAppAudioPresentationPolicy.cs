using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;
internal sealed class WhatsAppAudioPresentationPolicy(IWhatsAppSettingsReader settings,TimeProvider clock)
{
    public DateTime NowUtc => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// What the audios show, with the retention of Configuración › WhatsApp (RC3-A) read now: one read per use, the same for every audio of
    /// it, and the clock live.
    /// </summary>
    public async Task<WhatsAppAudioPresentation> ReadAsync(CancellationToken ct)=>new((await settings.FindAsync(ct)).MessageRetentionDays,clock);
}

/// <summary>The audios of one use, with the retention read for it: an audio's content lasts as long as its message's, or less.</summary>
internal sealed class WhatsAppAudioPresentation(int messageRetentionDays,TimeProvider clock)
{
    public DateTime NowUtc => clock.GetUtcNow().UtcDateTime;
    public DateTime ExpiresAtUtc(WhatsAppAudioSourceRow source)
    {
        var maximum=source.OccurredAtUtc.AddDays(messageRetentionDays);
        return source.ContentExpiresAtUtc is {} captured && captured<maximum?captured:maximum;
    }
    public bool IsExpired(WhatsAppAudioSourceRow source)=>ExpiresAtUtc(source)<=NowUtc;
    public WhatsAppAudioResponse View(WhatsAppAudioSourceRow source)=>new(IsExpired(source)?WhatsAppAudioState.Expired:source.State,
        !IsExpired(source) && source.StorageKey is not null, IsExpired(source) ? null : Reason(source.State, source.FailureCode));
    public WhatsAppAudioResponse? View(WhatsAppMessageRow message)
    {
        if(message.Kind!=WhatsAppMessageKind.Audio)return null;
        var expires=message.OccurredAtUtc.AddDays(messageRetentionDays);
        if(message.Audio?.ContentExpiresAtUtc is {} captured && captured<expires)expires=captured;
        return expires<=NowUtc?new(WhatsAppAudioState.Expired,false):new(message.Audio?.State??WhatsAppAudioState.Unavailable,message.Audio?.HasContent??false,
            Reason(message.Audio?.State??WhatsAppAudioState.Unavailable,message.Audio?.FailureCode));
    }
    private static string? Reason(WhatsAppAudioState state, string? failureCode) =>
        state == WhatsAppAudioState.Unavailable && failureCode == FileErrors.TooLarge.Code ? "tooLarge" : null;

    public string? VisibleText(WhatsAppMessageRow message)=>message.Kind!=WhatsAppMessageKind.Audio?message.Body:
        View(message)?.State==WhatsAppAudioState.Completed?message.Body:null;
}
