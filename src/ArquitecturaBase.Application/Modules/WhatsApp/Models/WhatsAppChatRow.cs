using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppChatRow
{
    public Guid Id { get; init; }
    public string? ProfileName { get; init; }
    public string? PhoneNumber { get; init; }
    public WhatsAppConversationMode Mode { get; init; }
    public long ModeRevision { get; init; }
    public DateTime LastInboundAtUtc { get; init; }
    public long LastInboundSequence { get; init; }
    public int UnreadCount { get; init; }
    public string? Preview { get; init; }
    public bool PreviewIsFormAction { get; init; }
    public bool PreviewIsFormReply { get; init; }
    public bool PreviewIsAudio { get; init; }
    public bool PreviewIsAssistant { get; init; }
    public WhatsAppMessagePurpose PreviewPurpose { get; init; }
    public WhatsAppAccessEventKind? PreviewAccessEventKind { get; init; }
}
public sealed record WhatsAppChatCounts(int AllCount, int UnreadCount);
public sealed record WhatsAppMessageRow(
    Guid Id, WhatsAppMessageDirection Direction, WhatsAppMessageKind Kind, string? Body, DateTime OccurredAtUtc,
    long InboundSequence, Guid? OperatorUserId, WhatsAppMessageStatus? Status, int? ErrorCode,
    WhatsAppMessagePurpose Purpose = WhatsAppMessagePurpose.Unclassified, WhatsAppAccessEventKind? AccessEventKind = null,
    WhatsAppFormPresentationRow? Form = null, WhatsAppFormReplyRow? FormReply = null,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation, WhatsAppAudioMessageRow? Audio = null)
{
    public WhatsAppAssistantPresentationRow? Assistant { get; init; }
    public string? QuickReplyTitlesJson { get; init; }
}
public sealed record WhatsAppAudioMessageRow(WhatsAppAudioState State, bool HasContent, DateTime? ContentExpiresAtUtc);
public sealed record WhatsAppFormReplyRow(string State, Guid? SourceReceiptId, Guid? DefinitionId, Guid? VersionId,
    long? VersionNumber, string? NameJson, string? Culture, Guid? RecordId, string? Number, string? RejectionCode = null, string? RejectedQuestion = null, int? FileCount = null);
public sealed record WhatsAppOutgoingMessageRow(
    Guid Id, Guid RequestId, Guid? OperatorUserId, string? Body, DateTime RequestedAtUtc,
    WhatsAppOutgoingMessageState State, string? WaMessageId,
    WhatsAppOutgoingMessageKind Kind = WhatsAppOutgoingMessageKind.Text, WhatsAppFormPresentationRow? Form = null,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation)
{
    public WhatsAppAssistantPresentationRow? Assistant { get; init; }
    public string? QuickRepliesJson { get; init; }
}
public sealed record WhatsAppThreadRow(
    WhatsAppChatRow Contact, IReadOnlyList<WhatsAppMessageRow> Messages,
    IReadOnlyList<WhatsAppOutgoingMessageRow> Outgoing, DateTime? BeforeAtUtc, Guid? BeforeId);

public sealed record WhatsAppAssistantPresentationRow(Guid SessionId, Guid ContactId, Guid UserId, Guid VersionId, byte[] ProtectedContent, DateTime ContentExpiresAtUtc)
{
    public override string ToString() => "WhatsAppAssistantPresentationRow { Content = [REDACTED] }";
}
