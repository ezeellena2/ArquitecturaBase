using System.Text;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// A received document: pending to be downloaded; stored with its copy, which stays in the chat until it is used; being read by the
/// engine; read; rejected with its reason; or not read: unavailable, failed, uncertain or expired. Only the first three change.
/// </summary>
public enum WhatsAppDocumentState { Pending, Stored, Reading, Read, Rejected, Unavailable, Failed, Uncertain, Expired }

// Task 8 of the documents plan: a photo (JPEG or PNG) or a document that is not a spreadsheet keeps Meta's descriptor from the webhook,
// like a spreadsheet. Its private copy is stored once, with the size, the SHA-256 read back and the pages of a PDF; a Word keeps the
// reading its owner prepared. It is read only once it is used: a Word with that reading, a PDF or a photo by the engine, with the
// request, the actor, the context and the link revision fixed once, like an audio's normalization. Download failures saturate like an
// audio's; a file that is not read keeps its reason, and a reading that ends without its text keeps its code.
public sealed partial class WhatsAppMessage
{
    /// <summary>The longest file name kept, as a spreadsheet's: the chat, the inbox and the engine read the same.</summary>
    public const int DocumentNameLength = SpreadsheetNameLength;

    /// <summary>The longest file name the column holds.</summary>
    public const int MaxDocumentFileNameLength = 255;

    /// <summary>The largest document copied: as much as the assistant attaches.</summary>
    public const long MaxDocumentBytes = 10 * 1024 * 1024;

    /// <summary>The most pages a PDF that is read may have.</summary>
    public const int MaxDocumentPages = 10;

    /// <summary>A reading, serialized: its text is bounded by its owner (a Word) or the engine; this only bounds the column.</summary>
    public const int MaxDocumentReadingLength = 128 * 1024;

    /// <summary>The context of the reading, as an audio's normalization keeps it.</summary>
    public const int MaxDocumentContextBytes = 16 * 1024;

    public WhatsAppDocumentState? DocumentState { get; private set; }
    public string? DocumentMediaId { get; private set; }
    public string? DocumentContentType { get; private set; }

    /// <summary>The SHA-256 Meta sent with the webhook, in lowercase hex.</summary>
    public string? DocumentSha256 { get; private set; }
    public string? DocumentFileName { get; private set; }
    public string? DocumentStorageKey { get; private set; }
    public long? DocumentSize { get; private set; }

    /// <summary>The SHA-256 computed from the bytes of the private copy, read back after writing it.</summary>
    public string? DocumentCopySha256 { get; private set; }

    /// <summary>The pages of a PDF, as its owner counted them; null for a photo or a Word.</summary>
    public int? DocumentPages { get; private set; }

    /// <summary>The reading: the one a Word's owner prepared, or the one the engine returned for a PDF or a photo.</summary>
    public string? DocumentReadingJson { get; private set; }
    public Guid? DocumentRequestId { get; private set; }
    public Guid? DocumentActorUserId { get; private set; }
    public string? DocumentContextJson { get; private set; }
    public long? DocumentLinkRevision { get; private set; }
    public DateTime? DocumentContentExpiresAtUtc { get; private set; }

    /// <summary>The reason a file is not read, the code of an unavailable document or reading, or the last download failure.</summary>
    public string? DocumentFailureCode { get; private set; }
    public int DocumentFailureCount { get; private set; }
    public DateTime? DocumentFirstFailureAtUtc { get; private set; }

    /// <summary>Until when a pending document without a request waits for one: the text or the audio that may follow it.</summary>
    public DateTime? DocumentRequestAwaitedUntilUtc { get; private set; }

    /// <summary>
    /// Fixes Meta's descriptor once. A descriptor that cannot be downloaded leaves the document visible, with its caption and its name,
    /// but unavailable.
    /// </summary>
    public bool SetDocumentDescriptor(string? mediaId, string? contentType, string? sha256, string? fileName)
    {
        if (Kind != WhatsAppMessageKind.ReceivedDocument || Direction != WhatsAppMessageDirection.Inbound) return false;
        var name = DocumentName(fileName, contentType);
        if (DocumentState is not null)
            return DocumentMediaId == mediaId && DocumentContentType == contentType
                && DocumentSha256 == sha256?.ToLowerInvariant() && DocumentFileName == name;
        DocumentFileName = name;
        if (mediaId is not { Length: > 0 and <= 128 } || !mediaId.All(char.IsAsciiDigit)
            || contentType is not { Length: > 0 and <= 128 } || contentType.Any(char.IsControl)
            || sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit))
        {
            DocumentState = WhatsAppDocumentState.Unavailable;
            return false;
        }

        DocumentMediaId = mediaId;
        DocumentContentType = contentType;
        DocumentSha256 = sha256.ToLowerInvariant();
        DocumentState = WhatsAppDocumentState.Pending;
        return true;
    }

    /// <summary>
    /// Keeps the private copy once, with the pages of a PDF and, for a Word, the reading its owner prepared; storing the same copy again
    /// only clears the failures.
    /// </summary>
    public Result StoreDocument(string storageKey, long size, string sha256, int? pages, string? preparedReadingJson)
    {
        if (DocumentState == WhatsAppDocumentState.Stored)
        {
            if (DocumentStorageKey != storageKey || DocumentSize != size || DocumentCopySha256 != sha256 || DocumentPages != pages
                || DocumentReadingJson != preparedReadingJson) return WhatsAppChatErrors.RequestConflict;
            ClearDocumentFailures();
            return Result.Success();
        }
        if (DocumentState != WhatsAppDocumentState.Pending || string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500
            || !StorableText.IsStorable(storageKey) || size is <= 0 or > MaxDocumentBytes
            || sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit) || pages is < 1 or > MaxDocumentPages
            || preparedReadingJson is not null && !IsReading(preparedReadingJson))
            return FileErrors.InvalidMetadata;
        DocumentStorageKey = storageKey;
        DocumentSize = size;
        DocumentCopySha256 = sha256;
        DocumentPages = pages;
        DocumentReadingJson = preparedReadingJson;
        DocumentState = WhatsAppDocumentState.Stored;
        ClearDocumentFailures();
        return Result.Success();
    }

    /// <summary>A pending document that is not read keeps its reason; true only the first time, the one that is answered.</summary>
    public bool RejectDocument(string reason)
    {
        if (DocumentState != WhatsAppDocumentState.Pending || !IsFailureCode(reason)) return false;
        DocumentState = WhatsAppDocumentState.Rejected;
        DocumentFailureCode = reason;
        return true;
    }

    /// <summary>
    /// A pending document without a request waits for one once, until a UTC instant, still pending: the text or the audio that follows
    /// it may be its request (decision 10). True only when it starts waiting.
    /// </summary>
    public bool AwaitDocumentRequest(DateTime untilUtc)
    {
        if (DocumentState != WhatsAppDocumentState.Pending || DocumentRequestAwaitedUntilUtc is not null || untilUtc.Kind != DateTimeKind.Utc) return false;
        DocumentRequestAwaitedUntilUtc = untilUtc;
        return true;
    }

    /// <summary>A pending document that will not be downloaded, with the code that says why.</summary>
    public void SetDocumentUnavailable(string code)
    {
        if (DocumentState != WhatsAppDocumentState.Pending || !IsFailureCode(code)) return;
        DocumentState = WhatsAppDocumentState.Unavailable;
        DocumentFailureCode = code;
    }

    /// <summary>
    /// A download failure that may pass: the document ends unavailable only with five failures and a day since the first (H11,
    /// <see cref="WhatsAppAcquisitionRetry"/>). True when it ends.
    /// </summary>
    public bool RegisterDocumentFailure(DateTime nowUtc, string code)
    {
        if (DocumentState != WhatsAppDocumentState.Pending || nowUtc.Kind != DateTimeKind.Utc || !IsFailureCode(code)) return false;
        DocumentFirstFailureAtUtc ??= nowUtc;
        DocumentFailureCount = WhatsAppAcquisitionRetry.Count(DocumentFailureCount);
        DocumentFailureCode = code;
        if (!WhatsAppAcquisitionRetry.GivesUp(DocumentFailureCount, DocumentFirstFailureAtUtc.Value, nowUtc)) return false;
        DocumentState = WhatsAppDocumentState.Unavailable;
        return true;
    }

    /// <summary>A stored Word that is used is read with what its owner prepared, without the engine.</summary>
    public Result UsePreparedDocumentReading()
    {
        if (DocumentState == WhatsAppDocumentState.Read && DocumentRequestId is null && DocumentReadingJson is not null) return Result.Success();
        if (DocumentState != WhatsAppDocumentState.Stored || DocumentReadingJson is null) return WhatsAppChatErrors.RequestConflict;
        DocumentState = WhatsAppDocumentState.Read;
        return Result.Success();
    }

    /// <summary>
    /// A stored PDF or photo that is used asks the engine for its reading once: the request, its actor, context and link revision and
    /// the expiry of its content are fixed then; asking again returns the same request.
    /// </summary>
    public Result<Guid> RequestDocumentReading(Guid actorUserId, string contextJson, long linkRevision, DateTime expiresAtUtc, DateTime nowUtc)
    {
        if (DocumentRequestId is { } previous) return previous;
        if (DocumentState != WhatsAppDocumentState.Stored || DocumentReadingJson is not null || actorUserId == Guid.Empty || linkRevision < 0
            || string.IsNullOrWhiteSpace(contextJson) || !StorableText.IsStorable(contextJson) || Encoding.UTF8.GetByteCount(contextJson) > MaxDocumentContextBytes
            || expiresAtUtc.Kind != DateTimeKind.Utc || expiresAtUtc <= nowUtc) return WhatsAppChatErrors.RequestConflict;
        DocumentRequestId = Guid.CreateVersion7();
        DocumentActorUserId = actorUserId;
        DocumentContextJson = contextJson;
        DocumentLinkRevision = linkRevision;
        DocumentContentExpiresAtUtc = expiresAtUtc;
        DocumentState = WhatsAppDocumentState.Reading;
        return DocumentRequestId.Value;
    }

    /// <summary>The engine's reading of its request, kept once while the content lasts; the same reading again is a replay.</summary>
    public Result CompleteDocumentReading(Guid requestId, string readingJson, DateTime nowUtc)
    {
        if (DocumentRequestId != requestId || DocumentContentExpiresAtUtc <= nowUtc) return WhatsAppChatErrors.RequestConflict;
        if (DocumentState == WhatsAppDocumentState.Read) return DocumentReadingJson == readingJson ? Result.Success() : WhatsAppChatErrors.RequestConflict;
        if (DocumentState != WhatsAppDocumentState.Reading || !IsReading(readingJson)) return FileErrors.InvalidMetadata;
        DocumentReadingJson = readingJson;
        DocumentState = WhatsAppDocumentState.Read;
        DocumentFailureCode = null;
        return Result.Success();
    }

    /// <summary>
    /// A reading in course that waits again, or ends without its text: failed, uncertain, expired or unavailable, with its code.
    /// Only a completed reading is read.
    /// </summary>
    public void SetDocumentReadingState(WhatsAppDocumentState state, string? code = null)
    {
        if (DocumentState != WhatsAppDocumentState.Reading || state is not (WhatsAppDocumentState.Reading or WhatsAppDocumentState.Failed
                or WhatsAppDocumentState.Uncertain or WhatsAppDocumentState.Expired or WhatsAppDocumentState.Unavailable)
            || code is not null && !IsFailureCode(code)) return;
        DocumentState = state;
        DocumentFailureCode = code;
    }

    /// <summary>
    /// The person's file name as the chat, the inbox and the engine take it, normalized once like a spreadsheet's: without folders or
    /// control characters and bounded, with «…» when it is cut. Without a name, one by its type.
    /// </summary>
    public static string DocumentName(string? fileName, string? contentType)
    {
        var name = fileName is null ? null
            : StorableText.Clean(string.Concat(Path.GetFileName(fileName.Replace('\\', '/')).Where(character => !char.IsControl(character))).Trim(), int.MaxValue);
        if (name is null or "." or "..") return DefaultDocumentName(contentType);
        if (name.Length <= DocumentNameLength) return name;
        var kept = DocumentNameLength - 1;
        if (char.IsHighSurrogate(name[kept - 1])) kept--;
        return name[..kept] + "…";
    }

    private static string DefaultDocumentName(string? contentType) => contentType?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "image/jpeg" => "image.jpg",
        "image/png" => "image.png",
        "application/pdf" => "document.pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => "document.docx",
        _ => "document",
    };

    private static bool IsReading(string json) => !string.IsNullOrWhiteSpace(json) && json.Length <= MaxDocumentReadingLength && StorableText.IsStorable(json);

    private void ClearDocumentFailures()
    {
        DocumentFailureCount = 0;
        DocumentFirstFailureAtUtc = null;
        DocumentFailureCode = null;
    }
}
