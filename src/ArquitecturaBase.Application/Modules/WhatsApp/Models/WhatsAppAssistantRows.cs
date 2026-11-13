using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// Whether a source is interpreted now (<see cref="Ready"/>), is a stored spreadsheet or a read document without a request
/// (<see cref="Alone"/>: a spreadsheet only as the answer a conversation waits for; a document also to be loaded, task 9), waits for its
/// pair (<see cref="Waiting"/>: a spreadsheet or a document still downloading or being read, or an audio still transcribing) or was
/// answered already (<see cref="Answered"/>: its spreadsheet or document was rejected with its reason).
/// </summary>
public enum WhatsAppAssistantSourceState { Ready, Alone, Waiting, Answered }

public sealed record WhatsAppAssistantSourceRow(Guid Id, Guid ContactId, string Body, DateTime OccurredAtUtc, DateTime ContentExpiresAtUtc)
{
    public Guid? CapturedRecordId {get;init;}
    public DateTime? AssistantProcessedAtUtc {get;init;}
    public string? WaMessageId {get;init;}
    /// <summary>IA, Stage 2: its kind and its place among the contact's messages, to find the contiguous one it pairs with.</summary>
    public WhatsAppMessageKind Kind {get;init;} = WhatsAppMessageKind.Text;
    public long InboundSequence {get;init;}
    /// <summary>The stored spreadsheet the request carries: the message's own or the contiguous one it pairs with.</summary>
    public WhatsAppAssistantSpreadsheetRow? Spreadsheet {get;init;}
    /// <summary>Task 9 of the documents: the read document the request carries, the message's own or the contiguous one, as a spreadsheet.</summary>
    public WhatsAppAssistantDocumentRow? Document {get;init;}
    public WhatsAppAssistantSourceState State {get;init;}
    public override string ToString() => "WhatsAppAssistantSourceRow { Content = [REDACTED] }";
}

/// <summary>
/// A read document as the request takes it (task 9): its message, its private copy (key, the normalized file name, size, SHA-256 of the
/// bytes and the pages of a PDF), the expiry of its content and its reading, serialized as the message keeps it.
/// </summary>
public sealed record WhatsAppAssistantDocumentRow(Guid MessageId, string StorageKey, string FileName, long Size, string Sha256, int? Pages,
    DateTime ExpiresAtUtc, string ReadingJson)
{
    /// <summary>When the person sent it, as Meta stamped it: what the inbox registers as its arrival.</summary>
    public DateTime ReceivedAtUtc { get; init; }
    public override string ToString() => "WhatsAppAssistantDocumentRow { Content = [REDACTED] }";
}

/// <summary>
/// A stored spreadsheet as the request takes it: its message, its private copy (key, the file name as the message keeps it, size and
/// SHA-256 of the bytes), the expiry of its content and the first sheet its owner prepared, serialized.
/// </summary>
public sealed record WhatsAppAssistantSpreadsheetRow(Guid MessageId, string StorageKey, string FileName, long Size, string Sha256,
    DateTime ExpiresAtUtc, string PreparedJson)
{
    public override string ToString() => "WhatsAppAssistantSpreadsheetRow { Content = [REDACTED] }";
}

/// <summary>
/// The business message just before a source, as far as pairing goes: a text's body, a completed audio's transcription while it
/// lasts or a spreadsheet's caption; its audio or spreadsheet state, a stored spreadsheet's copy, and whether an assistant reply was
/// requested after it and before the source.
/// </summary>
public sealed record WhatsAppAssistantNeighborRow(Guid Id, WhatsAppMessageKind Kind, DateTime OccurredAtUtc, DateTime ContentExpiresAtUtc)
{
    public string? Body {get;init;}
    public WhatsAppAudioState? AudioState {get;init;}
    public WhatsAppSpreadsheetState? SpreadsheetState {get;init;}
    public WhatsAppAssistantSpreadsheetRow? Spreadsheet {get;init;}
    /// <summary>A received document's state and, read, its copy and reading (task 9).</summary>
    public WhatsAppDocumentState? DocumentState {get;init;}
    /// <summary>Review of task 9: why a received document is unavailable (<c>quota</c> was answered with its fixed text).</summary>
    public string? DocumentFailureCode {get;init;}
    public WhatsAppAssistantDocumentRow? Document {get;init;}
    public bool AnsweredSince {get;init;}
    public override string ToString() => "WhatsAppAssistantNeighborRow { Content = [REDACTED] }";
}

public sealed record WhatsAppAssistantReferenceRow(Guid RequestId, Guid ContactId, Guid SourceMessageId, Guid? ActorUserId, Guid SubjectUserId,
    WhatsAppAssistantRequestKind Kind, long ModeRevision, long LinkRevision, string? ContextJson, DateTime ContentExpiresAtUtc,
    WhatsAppAssistantRequestState State, Guid? ProposalId, Guid? DefinitionId, Guid? OutgoingId)
{
    public Guid? QuestionOutgoingId {get;init;}
    public Guid? SourceRecordId {get;init;}
    public Guid? SuggestionSetId {get;init;}
    public Guid? SuggestionId {get;init;}
    public string? CapturedCulture {get;init;}
    public bool IsAlways {get;init;}
    /// <summary>The operation this one continues, and the spreadsheet message its request carries (IA, Stage 2).</summary>
    public Guid? PreviousRequestId {get;init;}
    public Guid? AttachmentMessageId {get;init;}
    public override string ToString() => "WhatsAppAssistantReferenceRow { Context = [REDACTED] }";
}

public sealed record WhatsAppAssistantChoiceRow(Guid Id,string WaMessageId,string ReplyId,DateTime OccurredAtUtc)
{
    public override string ToString()=>"WhatsAppAssistantChoiceRow { Identity = [REDACTED] }";
}
