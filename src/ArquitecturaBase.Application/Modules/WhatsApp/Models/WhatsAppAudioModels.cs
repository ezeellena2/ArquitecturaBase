using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppWebhookAudioDescriptor(string MediaId, string ContentType, string Sha256)
{
    public override string ToString() => "WhatsAppWebhookAudioDescriptor { Metadata = [REDACTED] }";
}
public sealed record WhatsAppAudioResponse(WhatsAppAudioState State, bool CanPlay);

public sealed record WhatsAppAudioSourceRow
{
    public Guid MessageId { get; init; }
    public Guid ContactId { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public WhatsAppConversationMode Mode { get; init; }
    public Guid? LinkedUserId { get; init; }
    public long LinkRevision { get; init; }
    public WhatsAppAudioState State { get; init; }
    public string? MediaId { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256 { get; init; }
    public string? StorageKey { get; init; }
    public long? Size { get; init; }
    public Guid? RequestId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ContextJson { get; init; }
    public long? CapturedLinkRevision { get; init; }
    public DateTime? ContentExpiresAtUtc { get; init; }
    public override string ToString() => "WhatsAppAudioSourceRow { Metadata and context = [REDACTED] }";
}

public sealed record WhatsAppAudioContentResponse(Stream Content,string ContentType,string Name)
{
    public override string ToString()=>"WhatsAppAudioContentResponse { Content = [REDACTED] }";
}
