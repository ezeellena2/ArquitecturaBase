using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>
/// The channel's private copy of a received document: its key, the person's file name, its media type, size, SHA-256 in lowercase and,
/// for a PDF, its pages. The owner reads it once to make its own copy.
/// </summary>
public sealed record StructuredDocumentCopy(string StorageKey, string Name, string MediaType, long Size, string Sha256, int? Pages)
{
    public override string ToString() => "StructuredDocumentCopy { Content = [REDACTED] }";
}

/// <summary>Why a received document was not read: its file cannot be opened, or the day's reading quota ran out.</summary>
public enum StructuredDocumentNotRead { Rejected, Quota }

/// <summary>
/// A document a person with a linked and active account sent (decision 9): who and when, the copy and, when it was read, its reading
/// and the interpretation's authentic proposal (the documents capability, or none when it closed without one). One that was not read
/// says why. The channel and its opaque source id register it once; the reply deadline is when the person stops being waited for.
/// </summary>
public sealed record StructuredDocumentRegistrationRequest(string ChannelId, string SourceId, Guid SenderUserId, string Culture,
    DateTime ReceivedAtUtc, StructuredDocumentCopy Copy)
{
    public StructuredDocumentReading? Reading { get; init; }
    public AssistantObservedProposal? Proposal { get; init; }
    public StructuredDocumentNotRead? NotRead { get; init; }
    /// <summary>The reason a file cannot be opened, when <see cref="NotRead"/> is Rejected (protected, legacy, too large…).</summary>
    public StructuredAttachmentRejection? Rejection { get; init; }
    public DateTime? ReplyExpiresAtUtc { get; init; }
    public override string ToString() => "StructuredDocumentRegistrationRequest { Content = [REDACTED] }";
}

/// <summary>
/// What to tell the person about a registered document (DOC2 to DOC4): the form is sure, two or three may fit, none was recognized,
/// a required datum cannot be read, it repeats another, the form cannot be sent to the person, the day's quota ran out, the file
/// cannot be opened, or the owner could not register it.
/// </summary>
public enum StructuredDocumentReplyKind { Sure, Doubt, Unknown, Unreadable, Duplicate, NotSendable, Quota, Rejected, Unavailable }

/// <summary>
/// The registered document and what to say, with the data of the texts, in the person's language: the forms (sure, one; doubt, two or
/// three; unknown, up to nine the person can complete), the standard's summary with its data when it has one, the required datum that
/// cannot be read and why, and when and in which response the repeated one was loaded.
/// </summary>
public sealed record StructuredDocumentRegistration(Guid? DocumentId, StructuredDocumentReplyKind Kind)
{
    public IReadOnlyList<StructuredDocumentFormChoice> Forms { get; init; } = [];
    public string? Summary { get; init; }
    public string? MissingDatum { get; init; }
    public StructuredDocumentProblem? Problem { get; init; }
    public StructuredDocumentDuplicate? Duplicate { get; init; }
    public bool WasAlreadyRegistered { get; init; }
    public override string ToString() => "StructuredDocumentRegistration { Content = [REDACTED] }";
}

/// <summary>A form a document may be loaded in, with its name in the person's language.</summary>
public sealed record StructuredDocumentFormChoice(Guid DefinitionId, Guid VersionId, string Name)
{
    public override string ToString() => "StructuredDocumentFormChoice { Content = [REDACTED] }";
}

/// <summary>Where the repeated document already is: when it was loaded (or received) and the response's number, when it is one.</summary>
public sealed record StructuredDocumentDuplicate(DateTime AtUtc, string? RecordNumber)
{
    public override string ToString() => "StructuredDocumentDuplicate { Content = [REDACTED] }";
}

/// <summary>The form a person chose for their document, to send it prepared with what was read.</summary>
public sealed record StructuredDocumentSendRequest(Guid DocumentId, string ChannelId, string SourceId, Guid RecipientUserId, string Culture,
    Guid DefinitionId)
{
    public override string ToString() => "StructuredDocumentSendRequest { Content = [REDACTED] }";
}

public enum StructuredDocumentSendState { Ready, NotSendable, Unavailable }

/// <summary>How a datum of the prepared form is marked (DOC3): read with doubts, not read, not passing its fixed validation, completed by the server.</summary>
public enum StructuredDocumentMarkKind { Doubtful, NotRead, Invalid, Completed }

public sealed record StructuredDocumentFieldMark(Guid FieldId, StructuredDocumentMarkKind Kind, string? ErrorCode = null)
{
    public override string ToString() => "StructuredDocumentFieldMark { Content = [REDACTED] }";
}

/// <summary>
/// The form prepared for the chosen candidate: its initial values and offers (as the assistant's prefill), the mark of each datum and the
/// question the document goes to, when the form has one that accepts it (DOC3: the document goes attached).
/// </summary>
public sealed record StructuredDocumentSendPreparation(StructuredDocumentSendState State)
{
    public StructuredFormPrefillResponse? Prefill { get; init; }
    public IReadOnlyList<StructuredDocumentFieldMark> Marks { get; init; } = [];
    public Guid? DocumentFieldId { get; init; }
    public override string ToString() => "StructuredDocumentSendPreparation { Content = [REDACTED] }";
}

/// <summary>The person chose «Otro» for their document: it stays unclassified and its type of document is noted as a missing form.</summary>
public sealed record StructuredDocumentChoiceRequest(Guid DocumentId, string ChannelId, string SourceId, Guid SenderUserId, string Culture,
    DateTime ChosenAtUtc)
{
    public override string ToString() => "StructuredDocumentChoiceRequest { Content = [REDACTED] }";
}

public enum StructuredDocumentChoiceState { Recorded, AlreadyRecorded, Unavailable }
