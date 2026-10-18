using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public sealed class AiAssistantRequestReceipt : Entity, IAuditable
{
    private AiAssistantRequestReceipt() { }
    public Guid OwnerUserId { get; private set; }
    public Guid RequestId { get; private set; }
    public string Fingerprint { get; private set; } = string.Empty;
    public string KeyId { get; private set; } = string.Empty;
    public Guid ConversationId { get; private set; }
    public Guid TurnId { get; private set; }
    public string? ChannelId { get; private set; }
    public bool? IsAudio { get; private set; }
    public DateTime AcceptedAtUtc { get; private set; }
    public DateOnly AcceptedDayUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }
    public static AiAssistantRequestReceipt Create(Guid ownerUserId, Guid requestId, string fingerprint, string keyId,
        Guid conversationId, Guid turnId, DateTime acceptedAtUtc, DateTime expiresAtUtc, string channelId, bool isAudio) => new()
    {
        OwnerUserId = ownerUserId, RequestId = requestId, Fingerprint = fingerprint, KeyId = keyId,
        ConversationId = conversationId, TurnId = turnId, AcceptedAtUtc = acceptedAtUtc, ChannelId = channelId, IsAudio = isAudio,
        AcceptedDayUtc = DateOnly.FromDateTime(acceptedAtUtc), ExpiresAtUtc = expiresAtUtc,
    };
    public override string ToString() => "AiAssistantRequestReceipt { fingerprint: [REDACTED] }";
}
