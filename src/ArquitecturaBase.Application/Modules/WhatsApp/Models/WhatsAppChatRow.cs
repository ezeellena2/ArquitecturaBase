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
    public bool PreviewIsFormReply { get; init; }
    public WhatsAppMessagePurpose PreviewPurpose { get; init; }
    public WhatsAppAccessEventKind? PreviewAccessEventKind { get; init; }
}
public sealed record WhatsAppChatCounts(int AllCount, int UnreadCount);
public sealed record WhatsAppMessageRow(
    Guid Id, WhatsAppMessageDirection Direction, WhatsAppMessageKind Kind, string? Body, DateTime OccurredAtUtc,
    long InboundSequence, Guid? OperatorUserId, WhatsAppMessageStatus? Status, int? ErrorCode,
    WhatsAppMessagePurpose Purpose = WhatsAppMessagePurpose.Unclassified, WhatsAppAccessEventKind? AccessEventKind = null,
    WhatsAppFormPresentationRow? Form = null, WhatsAppFormReplyRow? FormReply = null);
public sealed record WhatsAppFormReplyRow(string State, Guid? SourceReceiptId, Guid? DefinitionId, Guid? VersionId,
    long? VersionNumber, string? NameJson, string? Culture, Guid? RecordId, string? Number, string? RejectionCode = null, string? RejectedQuestion = null);
public sealed record WhatsAppOutgoingMessageRow(
    Guid Id, Guid RequestId, Guid OperatorUserId, string? Body, DateTime RequestedAtUtc,
    WhatsAppOutgoingMessageState State, string? WaMessageId,
    WhatsAppOutgoingMessageKind Kind = WhatsAppOutgoingMessageKind.Text, WhatsAppFormPresentationRow? Form = null);
public sealed record WhatsAppThreadRow(
    WhatsAppChatRow Contact, IReadOnlyList<WhatsAppMessageRow> Messages,
    IReadOnlyList<WhatsAppOutgoingMessageRow> Outgoing, DateTime? BeforeAtUtc, Guid? BeforeId);
