using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>How a datum of a proposal is marked (DOC3 and DOC5-A): read with doubts, not read, not passing its fixed validation, completed by the server.</summary>
public static class FormDocumentMarks
{
    public const string Doubtful = "doubtful";
    public const string NotRead = "notRead";
    public const string Invalid = "invalid";
    public const string Completed = "completed";
}

// What formularios keeps of a proposal after validating it without AI (decision 8), protected in its document: the outcome, the type
// of document, each candidate with the values of its questions and their marks, and the document or response it repeats.
public sealed record FormDocumentPreparedProposal(string Outcome, string? DocumentType, IReadOnlyList<FormDocumentPreparedCandidate> Candidates)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FormDocumentRepetition? Repetition { get; init; }
    public override string ToString() => "FormDocumentPreparedProposal { Content = [REDACTED] }";
}
public sealed record FormDocumentPreparedCandidate(Guid DefinitionId, Guid VersionId, long DefinitionRevision, IReadOnlyList<FormDocumentPreparedValue> Values)
{
    public override string ToString() => "FormDocumentPreparedCandidate { Content = [REDACTED] }";
}
// A question's value, canonical (null when nothing was read), with its mark; a choice keeps the option's text by language.
public sealed record FormDocumentPreparedValue(Guid FieldId, string Type, string? Value, string? Mark)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ErrorCode { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyDictionary<string, string>? Text { get; init; }
    /// <summary>A typed number the inbox is searched by: an amount, an account or CUIT that passes its validation, or the standard's operation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Searchable { get; init; }
    public override string ToString() => "FormDocumentPreparedValue { Content = [REDACTED] }";
}
// Where the same document already is: a response of the form (with its number) or another document of the inbox.
public sealed record FormDocumentRepetition(DateTime AtUtc, Guid? RecordId, string? RecordNumber, Guid? DocumentId)
{
    public override string ToString() => "FormDocumentRepetition { Content = [REDACTED] }";
}

// A form a document can be read for: its active version, sealed reading and the standard it came from.
public sealed record FormDocumentFormRow(Guid DefinitionId, Guid VersionId, long DefinitionRevision, string Prefix, string NameJson, string Culture,
    string SchemaJson, string? DocumentReadingJson, string? StandardKey, int? StandardVersion, string? StandardBindingsJson, bool AppOnly)
{
    public long VersionNumber { get; init; }
    public string SubmitLabelJson { get; init; } = "{}";
    public int QuestionCount { get; init; }
    public override string ToString() => "FormDocumentFormRow { Content = [REDACTED] }";
}
public sealed record FormDocumentCatalogNameRow(Guid CatalogId, string NameJson, string OriginalCulture);
// A version a proposal was prepared for: its form, sealed name, culture and schema.
public sealed record FormDocumentVersionRow(Guid VersionId, Guid DefinitionId, string NameJson, string Culture, string SchemaJson)
{
    public override string ToString() => "FormDocumentVersionRow { Content = [REDACTED] }";
}
// An active option of a catalog found by a lookup or by the entity code of an account, with its texts.
public sealed record FormDocumentCatalogItemRow(Guid ItemId, string Code, string? TextEs, string? TextEn)
{
    public override string ToString() => "FormDocumentCatalogItemRow { Content = [REDACTED] }";
}
// A repetition as the reader finds it: a response of the form or a document of the inbox, the earliest first.
public sealed record FormDocumentRepetitionRow(DateTime AtUtc, Guid? RecordId, long? Number, Guid? DocumentId);
// A datum that marks a repeated document (the standard's duplicate keys), with its question, type and canonical value.
public sealed record FormDocumentRepetitionKey(Guid FieldId, string Type, string Value)
{
    public override string ToString() => "FormDocumentRepetitionKey { Content = [REDACTED] }";
}
// The origin of a response passed from the inbox: who passed it, when, and the document.
public sealed record FormDocumentOriginRow(Guid SourceReceiptId, Guid ReviewerUserId, string? ReviewerName, DateTime ReviewedAtUtc, Guid DocumentId);

public sealed record FormReceivedDocumentStoredRow(Guid Id, string ChannelId, string SourceId, Guid SenderUserId, string? SenderName,
    DateTime ReceivedAtUtc, string StorageKey, string FileName, string MediaType, long Size, string Sha256, int? Pages,
    byte[]? ProtectedReading, byte[]? ProtectedProposal, Guid? DefinitionId, Guid? VersionId, string? DefinitionNameJson, string? Prefix,
    ArquitecturaBase.Domain.Modules.BusinessForms.Documents.FormReceivedDocumentState State, string? ReasonCode, string? DocumentType, Guid? RecordId, long? RecordNumber, Guid? ReviewedByUserId, string? ReviewerName,
    DateTime? ReviewedAtUtc, DateTime? ReplyExpiresAtUtc, bool NotSendable, DateTime? DeleteAfterUtc, Guid? ExtractionRequestId,
    Guid? ExtractionDefinitionId, DateTime? ExtractionRequestedAtUtc, long Revision)
{
    /// <summary>Who asked for the extraction that runs, in which language and time zone: the context its operation is read with.</summary>
    public Guid? ExtractionActorUserId { get; init; }
    public string? ExtractionCulture { get; init; }
    public string? ExtractionTimeZoneId { get; init; }
    public override string ToString() => "FormReceivedDocumentStoredRow { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentEventRow(Guid Id, DateTime AtUtc, string Kind, Guid? ActorUserId, string? ActorName, byte[]? ProtectedDetail)
{
    public override string ToString() => "FormReceivedDocumentEventRow { Content = [REDACTED] }";
}
// How many documents are in each state with the list's other filters; ForReview are the proposed ones nobody waits for.
public sealed record FormReceivedDocumentStateCounts(int Proposed, int ForReview, int Unclassified, int Confirmed, int Discarded);
public sealed record FormReceivedDocumentPageRow(IReadOnlyList<FormReceivedDocumentStoredRow> Items, int TotalCount, FormReceivedDocumentStateCounts Counts)
{
    public override string ToString() => "FormReceivedDocumentPageRow { Content = [REDACTED] }";
}

/// <summary>
/// The inbox, page by page, newest first: by form, by who sent it, by when it arrived (instants, the upper one exclusive, as the sheet)
/// and by state (proposed, forReview, unclassified, confirmed or discarded), and searched by the numbers read (an amount with or without
/// separators, a CUIT, an account or an operation, only its digits).
/// </summary>
public sealed record ListFormReceivedDocumentsRequest : PagedRequest
{
    public static readonly IReadOnlyCollection<string> SortableFields = ["receivedAtUtc"];
    public static readonly IReadOnlyCollection<string> States = ["proposed", "forReview", "unclassified", "confirmed", "discarded"];
    public Guid? DefinitionId { get; init; }
    public Guid? SenderUserId { get; init; }
    public DateTime? ReceivedFromUtc { get; init; }
    public DateTime? ReceivedToUtc { get; init; }
    public string? State { get; init; }
    public override string ToString() => "ListFormReceivedDocumentsRequest { Content = [REDACTED] }";
}
public sealed record ReviewFormReceivedDocumentRequest(long? ExpectedRevision);
public sealed record ChangeFormReceivedDocumentFormRequest(Guid? RequestId, Guid? DefinitionId, long? ExpectedRevision);
public sealed record PassFormReceivedDocumentRequest(Guid? RequestId, Guid? VersionId, long? ExpectedRevision, IReadOnlyList<FormEntryAnswer>? Answers)
{
    public override string ToString() => "PassFormReceivedDocumentRequest { Content = [REDACTED] }";
}

public sealed record FormReceivedDocumentPersonResponse(Guid UserId, string? DisplayName);
public sealed record FormReceivedDocumentFormResponse(Guid DefinitionId, string Name);
public sealed record FormReceivedDocumentRecordReference(Guid RecordId, string Number);
public sealed record FormReceivedDocumentFileInfoResponse(string Name, string MediaType, long Size, int? Pages);
// A datum the list shows to find a document: the question and its value as read.
public sealed record FormReceivedDocumentHighlightResponse(Guid FieldId, string Label, string Value)
{
    public override string ToString() => "FormReceivedDocumentHighlightResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentListItemResponse(Guid Id, DateTime ReceivedAtUtc, string ChannelId, FormReceivedDocumentPersonResponse Sender,
    FormReceivedDocumentFormResponse? Form, string State, bool ForReview, string? ReasonCode, string? DocumentType,
    FormReceivedDocumentRecordReference? Record, FormReceivedDocumentFileInfoResponse File, IReadOnlyList<FormReceivedDocumentHighlightResponse> Highlights,
    DateTime? DeleteAfterUtc, long Revision)
{
    public override string ToString() => "FormReceivedDocumentListItemResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentsPageResponse(IReadOnlyList<FormReceivedDocumentListItemResponse> Items, int Page, int PageSize, int TotalCount,
    FormReceivedDocumentStateCounts Counts)
{
    public override string ToString() => "FormReceivedDocumentsPageResponse { Content = [REDACTED] }";
}
// The menu's counter: the ones waiting for someone of the team, proposed for review here and unclassified.
public sealed record FormReceivedDocumentCountsResponse(int ForReview, int Unclassified, int Total);

public sealed record FormReceivedDocumentReadingResponse(string Readability, string? Problem, string? Language, string Text, bool Truncated)
{
    public override string ToString() => "FormReceivedDocumentReadingResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentValueResponse(Guid FieldId, string Label, string Type, bool Required, string? Value, string? Text, string? Mark,
    string? ErrorCode)
{
    public override string ToString() => "FormReceivedDocumentValueResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentCandidateResponse(Guid DefinitionId, Guid VersionId, string Name, IReadOnlyList<FormReceivedDocumentValueResponse> Values)
{
    public override string ToString() => "FormReceivedDocumentCandidateResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentProposalResponse(string Outcome, string? DocumentType, IReadOnlyList<FormReceivedDocumentCandidateResponse> Candidates,
    FormDocumentRepetition? Repetition)
{
    public override string ToString() => "FormReceivedDocumentProposalResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentExtractionInfoResponse(Guid RequestId, Guid DefinitionId, DateTime RequestedAtUtc);
public sealed record FormReceivedDocumentEventResponse(DateTime AtUtc, string Kind, FormReceivedDocumentPersonResponse? Actor, string? Detail)
{
    public override string ToString() => "FormReceivedDocumentEventResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentDetailResponse(FormReceivedDocumentListItemResponse Document, FormReceivedDocumentReadingResponse? Reading,
    FormReceivedDocumentProposalResponse? Proposal, FormReceivedDocumentExtractionInfoResponse? Extraction,
    IReadOnlyList<FormReceivedDocumentEventResponse> History, bool CanReview)
{
    public override string ToString() => "FormReceivedDocumentDetailResponse { Content = [REDACTED] }";
}
// A change of form while it runs (pending), after it (completed, with the document as it stayed) or when it failed, with its code.
public sealed record FormReceivedDocumentExtractionResponse(Guid RequestId, string State, string? FailureCode)
{
    public override string ToString() => "FormReceivedDocumentExtractionResponse { Content = [REDACTED] }";
}
public sealed record FormReceivedDocumentRecordResponse(Guid DocumentId, Guid RecordId, string Number, string State);
public sealed record FormReceivedDocumentFileResponse(Stream Content, string ContentType, string FileName)
{
    public override string ToString() => "FormReceivedDocumentFileResponse { Content = [REDACTED] }";
}

// The registration of a document as the inbox service receives it from its channel port, with the copy it made.
internal sealed record FormReceivedDocumentRegistration(Guid Id, StructuredDocumentRegistrationRequest Request, string StorageKey)
{
    public override string ToString() => "FormReceivedDocumentRegistration { Content = [REDACTED] }";
}
