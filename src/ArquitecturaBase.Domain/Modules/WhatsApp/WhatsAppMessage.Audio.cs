using System.Text;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public enum WhatsAppAudioState { Pending, Transcribing, Completed, Failed, Uncertain, Unavailable, Expired }

public sealed partial class WhatsAppMessage
{
    public WhatsAppAudioState? AudioState { get; private set; }
    public string? AudioMediaId { get; private set; }
    public string? AudioContentType { get; private set; }
    public string? AudioSha256 { get; private set; }
    public string? AudioStorageKey { get; private set; }
    public long? AudioSize { get; private set; }
    public Guid? AudioRequestId { get; private set; }
    public Guid? AudioActorUserId { get; private set; }
    public string? AudioContextJson { get; private set; }
    public long? AudioLinkRevision { get; private set; }
    public DateTime? AudioContentExpiresAtUtc { get; private set; }
    public string? AudioFailureCode { get; private set; }
    public int AudioFailureCount { get; private set; }
    public DateTime? AudioFirstFailureAtUtc { get; private set; }

    public bool SetAudioDescriptor(string? mediaId, string? contentType, string? sha256)
    {
        if (Kind != WhatsAppMessageKind.Audio || Direction != WhatsAppMessageDirection.Inbound) return false;
        if (AudioState is not null) return AudioMediaId == mediaId && AudioContentType == contentType && AudioSha256 == sha256;
        if (mediaId is not { Length: > 0 and <= 128 } || !mediaId.All(char.IsAsciiDigit) ||
            contentType is not { Length: > 0 and <= 128 } || contentType.Any(char.IsControl) ||
            !contentType.Split(';')[0].Trim().Equals("audio/ogg", StringComparison.OrdinalIgnoreCase) ||
            sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
        { AudioState = WhatsAppAudioState.Unavailable; return false; }
        AudioMediaId = mediaId; AudioContentType = contentType; AudioSha256 = sha256.ToLowerInvariant();
        AudioState = WhatsAppAudioState.Pending;
        return true;
    }
    public Result<Guid> RequestAudioNormalization(Guid actorUserId, string contextJson, long linkRevision, DateTime expiresAtUtc, DateTime nowUtc)
    {
        if (AudioRequestId is { } previous) return previous;
        if (AudioState != WhatsAppAudioState.Pending || actorUserId == Guid.Empty || linkRevision < 0 ||
            string.IsNullOrWhiteSpace(contextJson) || !StorableText.IsStorable(contextJson) || Encoding.UTF8.GetByteCount(contextJson) > 16384 ||
            expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= nowUtc) return WhatsAppChatErrors.RequestConflict;
        AudioRequestId = Guid.CreateVersion7(); AudioActorUserId = actorUserId; AudioContextJson = contextJson;
        AudioLinkRevision = linkRevision; AudioContentExpiresAtUtc = expiresAtUtc;
        return AudioRequestId.Value;
    }
    public Result StoreAudio(string storageKey, long size)
    {
        if (AudioStorageKey is not null)
        {
            if (AudioStorageKey != storageKey || AudioSize != size) return WhatsAppChatErrors.RequestConflict;
            AudioFailureCount = 0; AudioFirstFailureAtUtc = null; AudioFailureCode = null;
            return Result.Success();
        }
        if (AudioState != WhatsAppAudioState.Pending || string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500 ||
            !StorableText.IsStorable(storageKey) || size is <= 0 or > 25000000) return FileErrors.InvalidMetadata;
        AudioStorageKey = storageKey; AudioSize = size;
        AudioFailureCount = 0; AudioFirstFailureAtUtc = null; AudioFailureCode = null;
        return Result.Success();
    }
    public Result CompleteAudio(Guid requestId, string text, DateTime nowUtc)
    {
        if (AudioRequestId != requestId || AudioContentExpiresAtUtc <= nowUtc) return WhatsAppChatErrors.RequestConflict;
        if (AudioState == WhatsAppAudioState.Completed) return Body == text ? Result.Success() : WhatsAppChatErrors.RequestConflict;
        if (AudioState is not (WhatsAppAudioState.Pending or WhatsAppAudioState.Transcribing) || AudioStorageKey is null ||
            string.IsNullOrWhiteSpace(text) || text.Length > 4000 || !StorableText.IsStorable(text)) return FileErrors.InvalidMetadata;
        Body = text; AudioState = WhatsAppAudioState.Completed; AudioFailureCode = null;
        return Result.Success();
    }
    public void SetAudioState(WhatsAppAudioState state, string? code = null)
    {
        if (AudioState is not (WhatsAppAudioState.Pending or WhatsAppAudioState.Transcribing) || state == WhatsAppAudioState.Completed ||
            !Enum.IsDefined(state) || code?.Length > 128 || code is not null && !StorableText.IsStorable(code)) return;
        AudioState = state; AudioFailureCode = code;
    }
    public bool RegisterAudioFailure(DateTime nowUtc, string code)
    {
        if(AudioState is not (WhatsAppAudioState.Pending or WhatsAppAudioState.Transcribing) || nowUtc.Kind!=DateTimeKind.Utc
            || string.IsNullOrEmpty(code) || code.Length>128 || !StorableText.IsStorable(code))return false;
        AudioFirstFailureAtUtc ??= nowUtc;AudioFailureCount=Math.Min(5,AudioFailureCount+1);AudioFailureCode=code;
        if(AudioFailureCount<5 || nowUtc-AudioFirstFailureAtUtc.Value<TimeSpan.FromMinutes(15))return false;
        AudioState=WhatsAppAudioState.Unavailable;return true;
    }
}
