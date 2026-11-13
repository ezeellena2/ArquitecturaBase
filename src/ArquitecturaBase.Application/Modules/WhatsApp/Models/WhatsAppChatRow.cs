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
    public WhatsAppMessageKind? PreviewKind { get; init; }
    public bool PreviewIsAssistant { get; init; }
    public WhatsAppMessagePurpose PreviewPurpose { get; init; }
    public WhatsAppAccessEventKind? PreviewAccessEventKind { get; init; }
    /// <summary>Derivar (AS4): «Necesita una persona», with its reason, until a person answers.</summary>
    public string? HandoffReasonCode { get; init; }
    public string? HandoffReason { get; init; }
    public DateTime? HandoffAtUtc { get; init; }
}
public sealed record WhatsAppChatCounts(int AllCount, int UnreadCount, int NeedsPersonCount = 0);
public sealed record WhatsAppMessageRow(
    Guid Id, WhatsAppMessageDirection Direction, WhatsAppMessageKind Kind, string? Body, DateTime OccurredAtUtc,
    long InboundSequence, Guid? OperatorUserId, WhatsAppMessageStatus? Status, int? ErrorCode,
    WhatsAppMessagePurpose Purpose = WhatsAppMessagePurpose.Unclassified, WhatsAppAccessEventKind? AccessEventKind = null,
    WhatsAppFormPresentationRow? Form = null, WhatsAppFormReplyRow? FormReply = null,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation, WhatsAppAudioMessageRow? Audio = null)
{
    public WhatsAppAssistantPresentationRow? Assistant { get; init; }
    public string? QuickReplyTitlesJson { get; init; }
    /// <summary>Casos del asistente: «Por qué respondió esto», kept with the message once it went out.</summary>
    public string? AssistantReplyJson { get; init; }
    /// <summary>The normalized file name of a received spreadsheet (stage 2 of the assistant authoring).</summary>
    public string? SpreadsheetName { get; init; }
    /// <summary>W1-A: the point of a received location.</summary>
    public decimal? Latitude { get; init; }
    public decimal? Longitude { get; init; }
    /// <summary>Task 8 of the documents: what the chat shows of a received document.</summary>
    public WhatsAppReceivedDocumentMessageRow? Document { get; init; }
    /// <summary>W3: the first card of a shared contact, while its content lives.</summary>
    public string? ContactName { get; init; }
    public string? ContactPhonesJson { get; init; }
    /// <summary>
    /// Review of task 9 of the documents: a form the assistant sent for a file the person sent (its body may say what was read), hidden
    /// without <c>formrecords.read</c> like the body of a form action.
    /// </summary>
    public bool LoadsFile { get; init; }
    /// <summary>Review of task 9: the label of the button of a question that went as a list, from the send it came from.</summary>
    public string? ListButton { get; init; }
    /// <summary>W3 (task 24): what a resend of the form carried, while the retention keeps it; only a resend has it.</summary>
    public WhatsAppSharedPrefillRow? SharedPrefill { get; init; }
    /// <summary>VC1-A (task 29): «Por qué lo propuso» of the notice of the loads of a spreadsheet, with the ids of its forms.</summary>
    public string? SpreadsheetRowsJson { get; init; }
    // The body, the point and the contact are what the person wrote or shared: the generated ToString would print them.
    public override string ToString() => $"{nameof(WhatsAppMessageRow)} ({Kind})";
}
public sealed record WhatsAppAudioMessageRow(WhatsAppAudioState State, bool HasContent, DateTime? ContentExpiresAtUtc, string? FailureCode = null);
public sealed record WhatsAppFormReplyRow(string State, Guid? SourceReceiptId, Guid? DefinitionId, Guid? VersionId,
    long? VersionNumber, string? NameJson, string? Culture, Guid? RecordId, string? Number, string? RejectionCode = null, string? RejectedQuestion = null, int? FileCount = null)
{
    /// <summary>W3 (task 24): a reply rejected after its send already had its response, with the number of the one that was kept.</summary>
    public string? SavedNumber { get; init; }
}
public sealed record WhatsAppOutgoingMessageRow(
    Guid Id, Guid RequestId, Guid? OperatorUserId, string? Body, DateTime RequestedAtUtc,
    WhatsAppOutgoingMessageState State, string? WaMessageId,
    WhatsAppOutgoingMessageKind Kind = WhatsAppOutgoingMessageKind.Text, WhatsAppFormPresentationRow? Form = null,
    WhatsAppMessageOrigin Origin = WhatsAppMessageOrigin.Conversation)
{
    public WhatsAppAssistantPresentationRow? Assistant { get; init; }
    public string? QuickRepliesJson { get; init; }
    /// <summary>Casos del asistente: «Por qué respondió esto» of a queued reply.</summary>
    public string? AssistantReplyJson { get; init; }
    /// <summary>Review of task 9: a form sent for a file the person sent, whose body is hidden without <c>formrecords.read</c>.</summary>
    public bool LoadsFile { get; init; }
    /// <summary>Review of task 9: the label of the button of a question that goes as a list.</summary>
    public string? ListButton { get; init; }
    /// <summary>W3 (task 24): what a queued resend of the form carries, while the retention keeps it; only a resend has it.</summary>
    public WhatsAppSharedPrefillRow? SharedPrefill { get; init; }
    /// <summary>VC1-A (task 29): «Por qué lo propuso» of the queued notice of the loads of a spreadsheet.</summary>
    public string? SpreadsheetRowsJson { get; init; }
    /// <summary>H2: who marked an uncertain send as reviewed, and when.</summary>
    public Guid? ReviewedByUserId { get; init; }
    public DateTime? ReviewedAtUtc { get; init; }
}
public sealed record WhatsAppThreadRow(
    WhatsAppChatRow Contact, IReadOnlyList<WhatsAppMessageRow> Messages,
    IReadOnlyList<WhatsAppOutgoingMessageRow> Outgoing, DateTime? BeforeAtUtc, Guid? BeforeId)
{
    /// <summary>What the assistant decided without a message in the page's span («No responder», a handoff that sent nothing).</summary>
    public IReadOnlyList<WhatsAppAssistantEventRow> AssistantEvents { get; init; } = [];
}

/// <summary>A decision of the assistant that sent nothing: its reference, when and its why.</summary>
public sealed record WhatsAppAssistantEventRow(Guid RequestId, DateTime AtUtc, string OutcomeJson)
{
    public override string ToString() => "WhatsAppAssistantEventRow { Content = [REDACTED] }";
}

/// <summary>
/// W3 (task 24): the session of a resend, with what its send took (when and which questions, without values) and what was written in each
/// question, protected for the session.
/// </summary>
public sealed record WhatsAppSharedPrefillRow(Guid SessionId, Guid ContactId, Guid UserId, Guid VersionId, string SharedContentJson, byte[] ProtectedValues)
{
    public override string ToString() => "WhatsAppSharedPrefillRow { Content = [REDACTED] }";
}

public sealed record WhatsAppAssistantPresentationRow(Guid SessionId, Guid ContactId, Guid UserId, Guid VersionId, byte[] ProtectedContent, DateTime ContentExpiresAtUtc)
{
    public override string ToString() => "WhatsAppAssistantPresentationRow { Content = [REDACTED] }";
}
