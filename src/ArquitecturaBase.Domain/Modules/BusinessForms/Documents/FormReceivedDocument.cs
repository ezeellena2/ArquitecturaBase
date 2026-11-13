using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

/// <summary>
/// Where a received document is (DOC5-A): proposed for a form, waiting for its person or for the team; unclassified, without a form;
/// confirmed in a response; or discarded. Stored as text: never renumber.
/// </summary>
public enum FormReceivedDocumentState { Proposed, Unclassified, Confirmed, Discarded }

/// <summary>
/// Why a proposed one was not sent to its person (VC1-A): the form cannot be sent to them (CE4-A), or it is a row past the loads of its
/// spreadsheet. Stored as text: never renumber.
/// </summary>
public enum FormReceivedDocumentNotSentReason { NotSendable, OverLimit }

/// <summary>
/// What became of a row of a spreadsheet when it was registered (VC1-A): a load, one whose form cannot be sent to its person, one past the
/// loads, a repeated one, one that fits no form or one the model did not see. A repeated registration answers it, whatever the team did with
/// the row later. Stored as text: never renumber.
/// </summary>
public enum FormReceivedDocumentRowKind { Load, NotSendable, OverLimit, Repeated, Unmatched, Unseen }

/// <summary>
/// A document a person with a linked account sent through a channel, in the inbox «Documentos recibidos» (decision 9): its own private
/// copy, its reading and proposal (protected by Application), its state with its reason, the numbers to search it by and the response it
/// became. The channel and its opaque source id are unique: a document is registered once. A proposed one waits for its person until its
/// reply expires, and then it is for the team to review: computed when read (<see cref="IsForReview(DateTime)"/>), without another process. A
/// discarded or unclassified one is deleted <see cref="RetentionDays"/> days later with its reading and copy (<see cref="DeleteAfterUtc"/>);
/// a confirmed one follows its response. Every change made by a person advances <see cref="Revision"/>.
/// </summary>
public sealed class FormReceivedDocument : Entity, IAuditable
{
    public const int RetentionDays = 90;
    public const int MaxChannelIdLength = 32;
    public const int MaxSourceIdLength = 200;
    public const int MaxStorageKeyLength = 300;
    public const int MaxFileNameLength = 255;
    public const int MaxMediaTypeLength = 100;
    public const int MaxReasonCodeLength = 64;
    public const int MaxDocumentTypeLength = 60;
    public const int MaxSearchTextLength = 4000;
    public const int MaxPages = 10;
    public const int MaxTimeZoneIdLength = Users.AccountRules.TimeZoneIdMaxLength;
    public const int MaxSheetNameLength = 120;
    /// <summary>The last row a sheet of Excel has.</summary>
    public const int MaxRowNumber = 1_048_576;

    /// <summary>A person discarded it in the inbox.</summary>
    public const string DiscardedByReviewer = "reviewer";
    /// <summary>The person chose «Otro» in the channel: no form fits it.</summary>
    public const string ChoseOther = "other";

    private FormReceivedDocument() { }
    private FormReceivedDocument(Guid id) : base(id) { }

    public string ChannelId { get; private set; } = "";
    public string SourceId { get; private set; } = "";
    public Guid SenderUserId { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    /// <summary>The inbox's own copy; null for a file that cannot be opened and that the inbox does not keep (task 8).</summary>
    public string? StorageKey { get; private set; }
    public string FileName { get; private set; } = "";
    public string MediaType { get; private set; } = "";
    /// <summary>Null only without a copy, when the size was never known (over the limit before its bytes arrived).</summary>
    public long? Size { get; private set; }
    /// <summary>The SHA-256 of the copy, in lowercase; null without one.</summary>
    public string? Sha256 { get; private set; }
    public int? Pages { get; private set; }
    public byte[]? ProtectedReading { get; private set; }
    public byte[]? ProtectedProposal { get; private set; }
    public Guid? DefinitionId { get; private set; }
    public Guid? VersionId { get; private set; }
    public FormReceivedDocumentState State { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? DocumentType { get; private set; }
    public string SearchText { get; private set; } = "";
    public Guid? RecordId { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public DateTime? ReplyExpiresAtUtc { get; private set; }
    public bool NotSendable { get; private set; }
    /// <summary>Why it was not sent to its person, exactly while <see cref="NotSendable"/> (VC1-A).</summary>
    public FormReceivedDocumentNotSentReason? NotSentReason { get; private set; }
    /// <summary>
    /// VC1-A: a row of a spreadsheet is an entry of its own, with its sheet (only when its book has more than one) and its number as Excel
    /// numbers it; its copy is the spreadsheet's, which its other rows share. Both null in a document.
    /// </summary>
    public string? SheetName { get; private set; }
    public int? RowNumber { get; private set; }
    /// <summary>VC1-A: a row's place among the entries of its spreadsheet, in the order of its book, and what became of it there.</summary>
    public int? RowOrder { get; private set; }
    public FormReceivedDocumentRowKind? RowKind { get; private set; }
    public DateTime? DeleteAfterUtc { get; private set; }
    /// <summary>
    /// The extraction a reviewer asked for to change its form, while it runs: its request, its form, when it was asked and who asked, in
    /// which language and time zone, the context the engine's operation is always read with, whoever polls it.
    /// </summary>
    public Guid? ExtractionRequestId { get; private set; }
    public Guid? ExtractionDefinitionId { get; private set; }
    public DateTime? ExtractionRequestedAtUtc { get; private set; }
    public Guid? ExtractionActorUserId { get; private set; }
    public string? ExtractionCulture { get; private set; }
    public string? ExtractionTimeZoneId { get; private set; }
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    /// <summary>
    /// A document as it arrives, with the id its copy was named after, still to classify: the channel, its opaque source id, who sent it
    /// and when, and its copy (key, file name, media type, size, SHA-256 in lowercase and, for a PDF, its pages).
    /// </summary>
    public static Result<FormReceivedDocument> Receive(Guid id, string channelId, string sourceId, Guid senderUserId, DateTime receivedAtUtc,
        string storageKey, string fileName, string mediaType, long size, string sha256, int? pages, DateTime? replyExpiresAtUtc)
    {
        if (id == Guid.Empty || !Text(channelId, MaxChannelIdLength) || !Text(sourceId, MaxSourceIdLength) || senderUserId == Guid.Empty
            || receivedAtUtc.Kind != DateTimeKind.Utc || !Text(storageKey, MaxStorageKeyLength) || !Text(fileName, MaxFileNameLength)
            || !Text(mediaType, MaxMediaTypeLength) || size < 0 || sha256 is not { Length: 64 } || !sha256.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f')
            || pages is < 1 or > MaxPages || replyExpiresAtUtc is { Kind: not DateTimeKind.Utc })
            return FormReceivedDocumentErrors.ContentInvalid;
        return new FormReceivedDocument(id)
        {
            ChannelId = channelId, SourceId = sourceId, SenderUserId = senderUserId, ReceivedAtUtc = receivedAtUtc, StorageKey = storageKey,
            FileName = fileName, MediaType = mediaType, Size = size, Sha256 = sha256, Pages = pages, ReplyExpiresAtUtc = replyExpiresAtUtc,
            State = FormReceivedDocumentState.Unclassified,
        };
    }

    /// <summary>
    /// VC1-A: a row of a spreadsheet as it arrives, still to classify: like a document, with the copy of its spreadsheet (shared with its other
    /// rows, without pages), its sheet when the book has more than one, its number, its place among the entries of the book and what became of it.
    /// </summary>
    public static Result<FormReceivedDocument> ReceiveRow(Guid id, string channelId, string sourceId, Guid senderUserId, DateTime receivedAtUtc,
        string storageKey, string fileName, string mediaType, long size, string sha256, string? sheetName, int rowNumber, int rowOrder,
        FormReceivedDocumentRowKind rowKind, DateTime? replyExpiresAtUtc)
    {
        if (sheetName is not null && !Text(sheetName, MaxSheetNameLength) || rowNumber is < 1 or > MaxRowNumber || rowOrder < 1 || !Enum.IsDefined(rowKind))
            return FormReceivedDocumentErrors.ContentInvalid;
        var received = Receive(id, channelId, sourceId, senderUserId, receivedAtUtc, storageKey, fileName, mediaType, size, sha256, null, replyExpiresAtUtc);
        if (received.IsFailure) return received;
        received.Value.SheetName = sheetName;
        received.Value.RowNumber = rowNumber;
        received.Value.RowOrder = rowOrder;
        received.Value.RowKind = rowKind;
        return received;
    }

    /// <summary>
    /// A file that cannot be opened and that the inbox does not keep (decision 10, task 8): what its person sent (the file name, the media type
    /// the channel declared and the size when it was known), discarded at once with its reason, without a copy, a reading or a proposal.
    /// </summary>
    public static Result<FormReceivedDocument> ReceiveRejected(Guid id, string channelId, string sourceId, Guid senderUserId, DateTime receivedAtUtc,
        string fileName, string mediaType, long? size, string reasonCode, DateTime nowUtc)
    {
        if (id == Guid.Empty || !Text(channelId, MaxChannelIdLength) || !Text(sourceId, MaxSourceIdLength) || senderUserId == Guid.Empty
            || receivedAtUtc.Kind != DateTimeKind.Utc || !Text(fileName, MaxFileNameLength) || !Text(mediaType, MaxMediaTypeLength) || size is <= 0
            || !Code(reasonCode) || nowUtc.Kind != DateTimeKind.Utc)
            return FormReceivedDocumentErrors.ContentInvalid;
        return new FormReceivedDocument(id)
        {
            ChannelId = channelId, SourceId = sourceId, SenderUserId = senderUserId, ReceivedAtUtc = receivedAtUtc, FileName = fileName,
            MediaType = mediaType, Size = size, State = FormReceivedDocumentState.Discarded, ReasonCode = reasonCode,
            DeleteAfterUtc = nowUtc.AddDays(RetentionDays),
        };
    }

    /// <summary>Whether the inbox keeps the document's file.</summary>
    public bool IsStored => StorageKey is not null;

    /// <summary>Whether it is a row of a spreadsheet (VC1-A).</summary>
    public bool IsRow => RowNumber is not null;

    /// <summary>The reading and, with a proposal, the type of document and the numbers to search it by; set while it is registered.</summary>
    public Result Describe(byte[]? protectedReading, string? documentType, string searchText)
    {
        if (protectedReading is { Length: 0 } || documentType is not null && !Text(documentType, MaxDocumentTypeLength)
            || searchText is null || searchText.Length > MaxSearchTextLength || !StorableText.IsStorable(searchText))
            return FormReceivedDocumentErrors.ContentInvalid;
        ProtectedReading = protectedReading;
        DocumentType = documentType?.Trim();
        SearchText = searchText;
        return Result.Success();
    }

    /// <summary>Proposed for a form at registration, waiting for its person; one that cannot be sent to them is for the team at once.</summary>
    public Result Propose(Guid definitionId, Guid versionId, byte[] protectedProposal, bool notSendable) =>
        Propose(definitionId, versionId, protectedProposal, notSendable ? FormReceivedDocumentNotSentReason.NotSendable : null);

    /// <summary>
    /// Proposed for a form at registration, waiting for its person unless it is not sent to them, with the reason: the form cannot be sent to
    /// them, or (only a row, VC1-A) it is past the loads of its spreadsheet. Either is for the team at once.
    /// </summary>
    public Result Propose(Guid definitionId, Guid versionId, byte[] protectedProposal, FormReceivedDocumentNotSentReason? notSent)
    {
        if (definitionId == Guid.Empty || versionId == Guid.Empty || protectedProposal is not { Length: > 0 }
            || notSent is { } reason && (!Enum.IsDefined(reason) || reason == FormReceivedDocumentNotSentReason.OverLimit && !IsRow))
            return FormReceivedDocumentErrors.ContentInvalid;
        DefinitionId = definitionId; VersionId = versionId; ProtectedProposal = protectedProposal;
        NotSendable = notSent is not null; NotSentReason = notSent;
        State = FormReceivedDocumentState.Proposed; ReasonCode = null; DeleteAfterUtc = null;
        return Result.Success();
    }

    /// <summary>Unclassified at registration (not recognized, or over the day's reading quota), with the proposal when there is one.</summary>
    public Result LeaveUnclassified(string reasonCode, byte[]? protectedProposal, DateTime nowUtc)
    {
        if (!Code(reasonCode) || protectedProposal is { Length: 0 } || nowUtc.Kind != DateTimeKind.Utc) return FormReceivedDocumentErrors.ContentInvalid;
        ProtectedProposal = protectedProposal; State = FormReceivedDocumentState.Unclassified; ReasonCode = reasonCode;
        DeleteAfterUtc = nowUtc.AddDays(RetentionDays);
        return Result.Success();
    }

    /// <summary>Discarded at registration (it cannot be opened, it is not legible, or it repeats another), with its reason.</summary>
    public Result Reject(string reasonCode, byte[]? protectedProposal, DateTime nowUtc)
    {
        if (!Code(reasonCode) || protectedProposal is { Length: 0 } || nowUtc.Kind != DateTimeKind.Utc) return FormReceivedDocumentErrors.ContentInvalid;
        ProtectedProposal = protectedProposal; State = FormReceivedDocumentState.Discarded; ReasonCode = reasonCode;
        DeleteAfterUtc = nowUtc.AddDays(RetentionDays);
        return Result.Success();
    }

    /// <summary>
    /// For the team to review here: proposed and nobody waits for its person, because it cannot be sent to them, it has no reply to wait
    /// for or its reply expired.
    /// </summary>
    public bool IsForReview(DateTime nowUtc) => IsForReview(State, NotSendable, ReplyExpiresAtUtc, nowUtc);

    public static bool IsForReview(FormReceivedDocumentState state, bool notSendable, DateTime? replyExpiresAtUtc, DateTime nowUtc) =>
        state == FormReceivedDocumentState.Proposed && (notSendable || replyExpiresAtUtc is null || replyExpiresAtUtc <= nowUtc);

    /// <summary>Until when its person is waited for: while it is proposed and not yet for the team; null when nobody is waited for.</summary>
    public static DateTime? AwaitedUntil(FormReceivedDocumentState state, bool notSendable, DateTime? replyExpiresAtUtc, DateTime nowUtc) =>
        state == FormReceivedDocumentState.Proposed && !IsForReview(state, notSendable, replyExpiresAtUtc, nowUtc) ? replyExpiresAtUtc : null;

    /// <summary>
    /// A reviewer discards a proposed or unclassified one, never while its person can still complete it; it can be reopened until it is
    /// deleted.
    /// </summary>
    public Result Discard(long expectedRevision, DateTime nowUtc)
    {
        if (Changed(expectedRevision) is { } changed) return changed;
        if (State is not (FormReceivedDocumentState.Proposed or FormReceivedDocumentState.Unclassified)) return FormReceivedDocumentErrors.StateConflict;
        if (AwaitsSender(nowUtc)) return FormReceivedDocumentErrors.AwaitingSender;
        State = FormReceivedDocumentState.Discarded; ReasonCode = DiscardedByReviewer; DeleteAfterUtc = nowUtc.AddDays(RetentionDays);
        ClearExtraction();
        return Advance();
    }

    /// <summary>A reviewer reopens a discarded one: proposed again if it had a form, unclassified otherwise; nobody waits for its person.</summary>
    public Result Reopen(long expectedRevision, DateTime nowUtc)
    {
        if (Changed(expectedRevision) is { } changed) return changed;
        if (State != FormReceivedDocumentState.Discarded) return FormReceivedDocumentErrors.StateConflict;
        if (DefinitionId is not null && ProtectedProposal is not null)
        {
            State = FormReceivedDocumentState.Proposed; ReasonCode = null; DeleteAfterUtc = null; ReplyExpiresAtUtc = null;
        }
        else
        {
            State = FormReceivedDocumentState.Unclassified; ReasonCode = null; DeleteAfterUtc = nowUtc.AddDays(RetentionDays);
        }
        return Advance();
    }

    /// <summary>
    /// A reviewer asks to extract it again for another form, from its reading, never while its person can still complete it; the request
    /// keeps who asked, in which language and time zone, and waits until it is applied or fails.
    /// </summary>
    public Result RequestExtraction(long expectedRevision, Guid requestId, Guid definitionId, Guid actorUserId, string culture, string timeZoneId,
        DateTime nowUtc)
    {
        if (Changed(expectedRevision) is { } changed) return changed;
        if (requestId == Guid.Empty || definitionId == Guid.Empty || actorUserId == Guid.Empty || culture is not ("es" or "en")
            || !Text(timeZoneId, MaxTimeZoneIdLength) || nowUtc.Kind != DateTimeKind.Utc) return FormReceivedDocumentErrors.ContentInvalid;
        if (State is not (FormReceivedDocumentState.Proposed or FormReceivedDocumentState.Unclassified)) return FormReceivedDocumentErrors.StateConflict;
        if (AwaitsSender(nowUtc)) return FormReceivedDocumentErrors.AwaitingSender;
        if (ProtectedReading is null) return FormReceivedDocumentErrors.ReadingUnavailable;
        ExtractionRequestId = requestId; ExtractionDefinitionId = definitionId; ExtractionRequestedAtUtc = nowUtc;
        ExtractionActorUserId = actorUserId; ExtractionCulture = culture; ExtractionTimeZoneId = timeZoneId;
        return Advance();
    }

    /// <summary>
    /// The extraction asked for came back with its proposal: proposed for that form, for the team to review here (the reply to its person no
    /// longer applies).
    /// </summary>
    public Result ApplyExtraction(Guid requestId, Guid versionId, byte[] protectedProposal, string? documentType, string searchText)
    {
        if (ExtractionRequestId != requestId || ExtractionDefinitionId is not { } definitionId) return FormReceivedDocumentErrors.ExtractionNotFound;
        if (State is not (FormReceivedDocumentState.Proposed or FormReceivedDocumentState.Unclassified)) return FormReceivedDocumentErrors.StateConflict;
        if (versionId == Guid.Empty || protectedProposal is not { Length: > 0 }) return FormReceivedDocumentErrors.ContentInvalid;
        var described = Describe(ProtectedReading, documentType, searchText);
        if (described.IsFailure) return described;
        DefinitionId = definitionId; VersionId = versionId; ProtectedProposal = protectedProposal;
        State = FormReceivedDocumentState.Proposed; ReasonCode = null; DeleteAfterUtc = null; ReplyExpiresAtUtc = null;
        ClearExtraction();
        return Advance();
    }

    /// <summary>The extraction asked for failed: the document stays as it was, without the request.</summary>
    public Result FailExtraction(Guid requestId)
    {
        if (ExtractionRequestId != requestId) return FormReceivedDocumentErrors.ExtractionNotFound;
        ClearExtraction();
        return Advance();
    }

    /// <summary>
    /// It became a response: confirmed with its record, never deleted with the inbox. A reviewer who passed it to the form is recorded with
    /// the moment; a person who confirmed it in the channel is not a reviewer.
    /// </summary>
    public Result Confirm(Guid recordId, Guid definitionId, Guid versionId, Guid? reviewerUserId, DateTime nowUtc)
    {
        if (recordId == Guid.Empty || definitionId == Guid.Empty || versionId == Guid.Empty || reviewerUserId == Guid.Empty
            || nowUtc.Kind != DateTimeKind.Utc) return FormReceivedDocumentErrors.ContentInvalid;
        if (State != FormReceivedDocumentState.Proposed) return FormReceivedDocumentErrors.StateConflict;
        // The form and version of the response it became, which may be a newer version than the one it was proposed for.
        RecordId = recordId; DefinitionId = definitionId; VersionId = versionId;
        State = FormReceivedDocumentState.Confirmed; ReasonCode = null; DeleteAfterUtc = null;
        if (reviewerUserId is { } reviewer) { ReviewedByUserId = reviewer; ReviewedAtUtc = nowUtc; }
        ClearExtraction();
        return Advance();
    }

    /// <summary>
    /// The person chose in the channel another of the forms its proposal named (Application checks it is one): the document follows that
    /// form, and a reviewer who saw the previous one gets a revision conflict.
    /// </summary>
    public Result Choose(Guid definitionId, Guid versionId)
    {
        if (definitionId == Guid.Empty || versionId == Guid.Empty) return FormReceivedDocumentErrors.ContentInvalid;
        if (State != FormReceivedDocumentState.Proposed) return FormReceivedDocumentErrors.StateConflict;
        DefinitionId = definitionId; VersionId = versionId;
        return Advance();
    }

    /// <summary>
    /// Task 9 of the documents: an unclassified document whose person chose a form in the channel, recognized for that form: proposed with the
    /// new proposal, its type and numbers and the deadline of the new send, or one that cannot be sent to them, for the team at once.
    /// </summary>
    public Result Recognize(Guid definitionId, Guid versionId, byte[] protectedProposal, string? documentType, string searchText, bool notSendable,
        DateTime? replyExpiresAtUtc)
    {
        if (State != FormReceivedDocumentState.Unclassified || ReasonCode == ChoseOther || ExtractionRequestId is not null)
            return FormReceivedDocumentErrors.StateConflict;
        if (replyExpiresAtUtc is { Kind: not DateTimeKind.Utc }) return FormReceivedDocumentErrors.ContentInvalid;
        var described = Describe(ProtectedReading, documentType, searchText);
        if (described.IsFailure) return described;
        var proposed = Propose(definitionId, versionId, protectedProposal, notSendable);
        if (proposed.IsFailure) return proposed;
        ReplyExpiresAtUtc = replyExpiresAtUtc;
        return Advance();
    }

    /// <summary>
    /// VC1-A: a spreadsheet none of whose rows fit (its single entry, unclassified, with its copy and without pages) whose person chose a form in
    /// the channel becomes the first of its rows for that form: its sheet when the book has more than one, its number, its place among the
    /// entries of the book, what became of it and the deadline of the new loads when it is one. Its caller classifies it as a row afterwards.
    /// </summary>
    public Result RecognizeRow(string? sheetName, int rowNumber, int rowOrder, FormReceivedDocumentRowKind rowKind, DateTime? replyExpiresAtUtc)
    {
        if (IsRow || !IsStored || Pages is not null || State != FormReceivedDocumentState.Unclassified || ReasonCode == ChoseOther || ExtractionRequestId is not null)
            return FormReceivedDocumentErrors.StateConflict;
        if (sheetName is not null && !Text(sheetName, MaxSheetNameLength) || rowNumber is < 1 or > MaxRowNumber || rowOrder < 1 || !Enum.IsDefined(rowKind)
            || replyExpiresAtUtc is { Kind: not DateTimeKind.Utc })
            return FormReceivedDocumentErrors.ContentInvalid;
        SheetName = sheetName; RowNumber = rowNumber; RowOrder = rowOrder; RowKind = rowKind; ReplyExpiresAtUtc = replyExpiresAtUtc;
        return Advance();
    }

    /// <summary>Task 9: an unclassified document recognized for the form its person chose, but not legible or repeated: discarded with its reason.</summary>
    public Result RejectRecognized(string reasonCode, byte[]? protectedProposal, DateTime nowUtc)
    {
        if (State != FormReceivedDocumentState.Unclassified || ReasonCode == ChoseOther || ExtractionRequestId is not null)
            return FormReceivedDocumentErrors.StateConflict;
        var rejected = Reject(reasonCode, protectedProposal, nowUtc);
        return rejected.IsFailure ? rejected : Advance();
    }

    /// <summary>The person chose «Otro» in the channel: unclassified, so the missing form is noted by its type of document.</summary>
    public Result ChooseOther(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) return FormReceivedDocumentErrors.ContentInvalid;
        if (State is not (FormReceivedDocumentState.Proposed or FormReceivedDocumentState.Unclassified)) return FormReceivedDocumentErrors.StateConflict;
        State = FormReceivedDocumentState.Unclassified; ReasonCode = ChoseOther; DeleteAfterUtc = nowUtc.AddDays(RetentionDays);
        ClearExtraction();
        return Advance();
    }

    public override string ToString() => "FormReceivedDocument { Content = [REDACTED] }";

    private Result? Changed(long expectedRevision) =>
        expectedRevision != Revision ? Result.Failure(FormReceivedDocumentErrors.RevisionConflict) : null;

    private Result Advance()
    {
        if (Revision >= 9007199254740991) return FormReceivedDocumentErrors.RevisionConflict;
        Revision++;
        return Result.Success();
    }

    // Proposed and its person can still complete it in the channel: nobody else acts on it meanwhile, so their capture is never lost.
    private bool AwaitsSender(DateTime nowUtc) => State == FormReceivedDocumentState.Proposed && !IsForReview(nowUtc);

    private void ClearExtraction()
    {
        ExtractionRequestId = null; ExtractionDefinitionId = null; ExtractionRequestedAtUtc = null;
        ExtractionActorUserId = null; ExtractionCulture = null; ExtractionTimeZoneId = null;
    }

    private static bool Code(string? value) => value is { Length: > 0 and <= MaxReasonCodeLength } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-');

    private static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && StorableText.IsStorable(value);
}
