using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public record WhatsAppChatResponse(
    Guid Id, string? ProfileName, string? PhoneNumber, WhatsAppConversationMode Mode, long ModeRevision,
    DateTime LastInboundAtUtc, long LastInboundSequence, int UnreadCount, string? Preview,
    WhatsAppMessagePurpose PreviewPurpose = WhatsAppMessagePurpose.Unclassified, WhatsAppAccessEventKind? PreviewAccessEventKind = null);

public sealed record WhatsAppChatsResponse(PagedResult<WhatsAppChatResponse> Page, int AllCount, int UnreadCount);

public sealed record WhatsAppMessageResponse(
    Guid Id, WhatsAppMessageDirection Direction, WhatsAppMessageKind Kind, string? Body, DateTime OccurredAtUtc,
    long InboundSequence, Guid? OperatorUserId, WhatsAppMessageStatus? Status, int? ErrorCode,
    WhatsAppMessagePurpose Purpose = WhatsAppMessagePurpose.Unclassified, WhatsAppAccessEventKind? AccessEventKind = null,
    WhatsAppFormPresentationResponse? Form = null, WhatsAppFormReplyResponse? FormReply = null,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation);
public sealed record WhatsAppFormReplyResponse(string State, Guid? DefinitionId, Guid? VersionId, long? VersionNumber,
    string? Name, Guid? RecordId, string? Number, IReadOnlyList<WhatsAppFormReplyValueResponse>? Values, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RejectionCode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RejectedQuestion = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? FileCount = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DateTime? ReceivedAtUtc = null);
public sealed record WhatsAppFormReplyValueResponse(Guid FieldId, string Key, string Type, string Label, object? Value);

public sealed record WhatsAppOutgoingMessageResponse(
    Guid Id, Guid RequestId, Guid? OperatorUserId, string? Body, DateTime RequestedAtUtc,
    WhatsAppOutgoingMessageState State, string? WaMessageId, WhatsAppFormPresentationResponse? Form = null,
    string? RecoveryPath = null, WhatsAppOutgoingMessageKind Kind = WhatsAppOutgoingMessageKind.Text,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation);

public sealed record WhatsAppThreadResponse(
    WhatsAppThreadContactResponse Contact, IReadOnlyList<WhatsAppMessageResponse> Messages,
    IReadOnlyList<WhatsAppOutgoingMessageResponse> Outgoing, DateTime? BeforeAtUtc, Guid? BeforeId,
    bool CanReadFormRecords = false);

public sealed record ListWhatsAppMessagesRequest(Guid ContactId, DateTime? BeforeAtUtc = null, Guid? BeforeId = null, int Limit = 50);
public sealed record SendWhatsAppTextRequest(Guid ContactId, Guid RequestId, string? Body);
public sealed record ChangeWhatsAppModeRequest(Guid ContactId, WhatsAppConversationMode Mode, long ExpectedRevision);
public sealed record MarkWhatsAppChatReadRequest(Guid ContactId, long ThroughSequence);
