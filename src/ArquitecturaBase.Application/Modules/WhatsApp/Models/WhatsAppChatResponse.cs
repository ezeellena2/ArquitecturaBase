using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public record WhatsAppChatResponse(
    Guid Id, string? ProfileName, string? PhoneNumber, WhatsAppConversationMode Mode, long ModeRevision,
    DateTime LastInboundAtUtc, long LastInboundSequence, int UnreadCount, string? Preview,
    WhatsAppMessagePurpose PreviewPurpose = WhatsAppMessagePurpose.Unclassified, WhatsAppAccessEventKind? PreviewAccessEventKind = null,
    // Derivar (AS4): only a conversation that needs a person carries it.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WhatsAppNeedsPersonResponse? NeedsPerson = null);

/// <summary>
/// «Necesita una persona»: the stable code of the reason (a case, the daily limit, an owner's own reason), the reason the business
/// wrote in a case, as written, and since when.
/// </summary>
public sealed record WhatsAppNeedsPersonResponse(string ReasonCode, string? Reason, DateTime AtUtc);

public sealed record WhatsAppChatsResponse(PagedResult<WhatsAppChatResponse> Page, int AllCount, int UnreadCount, int NeedsPersonCount = 0);

public sealed record WhatsAppMessageResponse(
    Guid Id, WhatsAppMessageDirection Direction, WhatsAppMessageKind Kind, string? Body, DateTime OccurredAtUtc,
    long InboundSequence, Guid? OperatorUserId, WhatsAppMessageStatus? Status, int? ErrorCode,
    WhatsAppMessagePurpose Purpose = WhatsAppMessagePurpose.Unclassified, WhatsAppAccessEventKind? AccessEventKind = null,
    WhatsAppFormPresentationResponse? Form = null, WhatsAppFormReplyResponse? FormReply = null,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation,
    WhatsAppAssistantExplanationResponse? Assistant = null, IReadOnlyList<string>? QuickReplies = null, WhatsAppAudioResponse? Audio = null,
    // Only a received spreadsheet carries its normalized name; the other messages keep their shape.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? SpreadsheetName = null,
    // W1-A: only a received location carries its point.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WhatsAppLocationPointResponse? Location = null,
    // Casos del asistente: only a reply of the agent carries «Por qué respondió esto».
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WhatsAppAssistantReplyResponse? AssistantReply = null,
    // Task 8 of the documents: only a received document carries its name, kind and pages while its descriptor lives.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WhatsAppReceivedDocumentResponse? Document = null,
    // W3: only a shared contact carries its first card, while its content lives.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WhatsAppSharedContactResponse? Contact = null,
    // Review of task 9 of the documents: only a question that went as a list carries the label of its button, and only what the assistant
    // sent for a file the person sent says so (its body, which may carry what was read, needs formrecords.read).
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ListButton = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool LoadsFile = false);
public sealed record WhatsAppLocationPointResponse(decimal Latitude, decimal Longitude)
{
    public override string ToString() => "WhatsAppLocationPointResponse { Content = [REDACTED] }";
}

/// <summary>
/// W3: the first card of a shared contact, as it came: its formatted name (null when it has none, never empty) and up to ten phones, each
/// one a line in the chat.
/// </summary>
public sealed record WhatsAppSharedContactResponse(string? Name, IReadOnlyList<string> Phones)
{
    public override string ToString() => "WhatsAppSharedContactResponse { Content = [REDACTED] }";
}
public sealed record WhatsAppFormReplyResponse(string State, Guid? DefinitionId, Guid? VersionId, long? VersionNumber,
    string? Name, Guid? RecordId, string? Number, IReadOnlyList<WhatsAppFormReplyValueResponse>? Values, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RejectionCode = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RejectedQuestion = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? FileCount = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] DateTime? ReceivedAtUtc = null)
{
    /// <summary>The mark the chat shows when the response was later corrected («Replaced») or voided («Voided»).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Amendment { get; init; }
}
public sealed record WhatsAppFormReplyValueResponse(Guid FieldId, string Key, string Type, string Label, object? Value);

public sealed record WhatsAppOutgoingMessageResponse(
    Guid Id, Guid RequestId, Guid? OperatorUserId, string? Body, DateTime RequestedAtUtc,
    WhatsAppOutgoingMessageState State, string? WaMessageId, WhatsAppFormPresentationResponse? Form = null,
    string? RecoveryPath = null, WhatsAppOutgoingMessageKind Kind = WhatsAppOutgoingMessageKind.Text,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation,
    WhatsAppAssistantExplanationResponse? Assistant = null, IReadOnlyList<string>? QuickReplies = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] WhatsAppAssistantReplyResponse? AssistantReply = null,
    // Review of task 9 of the documents: as in the messages, the button of a list and the mark of a send for a file.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? ListButton = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool LoadsFile = false);

/// <summary>A line of the chat for what the assistant decided without a message, with its why.</summary>
public sealed record WhatsAppAssistantEventResponse(Guid Id, DateTime AtUtc, WhatsAppAssistantReplyResponse Reply);

public sealed record WhatsAppThreadResponse(
    WhatsAppThreadContactResponse Contact, IReadOnlyList<WhatsAppMessageResponse> Messages,
    IReadOnlyList<WhatsAppOutgoingMessageResponse> Outgoing, DateTime? BeforeAtUtc, Guid? BeforeId,
    bool CanReadFormRecords = false)
{
    public required AssistantAvailabilityResponse AssistantAvailability { get; init; }
    public WhatsAppAssistantSuggestionPointerResponse? AssistantSuggestion { get; init; }
    /// <summary>
    /// Casos del asistente: «Sin respuesta del asistente» and handoffs that sent nothing, in the page's span, oldest first; absent
    /// when there is none.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WhatsAppAssistantEventResponse>? AssistantEvents { get; init; }
}

public sealed record ListWhatsAppMessagesRequest(Guid ContactId, DateTime? BeforeAtUtc = null, Guid? BeforeId = null, int Limit = 50);
public sealed record SendWhatsAppTextRequest(Guid ContactId, Guid RequestId, string? Body);
public sealed record ChangeWhatsAppModeRequest(Guid ContactId, WhatsAppConversationMode Mode, long ExpectedRevision);
public sealed record MarkWhatsAppChatReadRequest(Guid ContactId, long ThroughSequence);
