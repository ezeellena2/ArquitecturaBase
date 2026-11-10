using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// Una planilla recibida: pendiente de bajar, guardada con su copia y lo preparado, rechazada con su motivo o no disponible.
/// Las tres últimas son finales.
/// </summary>
public enum WhatsAppSpreadsheetState { Pending, Stored, Rejected, Unavailable }

// Stage 2 of the assistant authoring: a spreadsheet that arrives by WhatsApp keeps Meta's descriptor (media id, type, SHA-256
// and file name) from the webhook. Its private copy and its first sheet as the owner prepared it are stored once, with the size
// and the SHA-256 computed from the bytes; a file that is not read keeps its reason. Download failures saturate like an audio's.
public sealed partial class WhatsAppMessage
{
    /// <summary>The longest file name kept from Meta, for the operator to see it.</summary>
    public const int MaxSpreadsheetFileNameLength = 255;

    /// <summary>The largest spreadsheet copied: as much as a catalog import reads.</summary>
    public const long MaxSpreadsheetBytes = 10 * 1024 * 1024;

    /// <summary>The prepared sheet, serialized: its owner keeps it within 12 KiB.</summary>
    public const int MaxSpreadsheetPreparedLength = 16 * 1024;

    public WhatsAppSpreadsheetState? SpreadsheetState { get; private set; }
    public string? SpreadsheetMediaId { get; private set; }
    public string? SpreadsheetContentType { get; private set; }

    /// <summary>The SHA-256 Meta sent with the webhook, in lowercase hex.</summary>
    public string? SpreadsheetSha256 { get; private set; }
    public string? SpreadsheetFileName { get; private set; }
    public string? SpreadsheetStorageKey { get; private set; }
    public long? SpreadsheetSize { get; private set; }

    /// <summary>The SHA-256 computed from the bytes of the private copy, read back after writing it.</summary>
    public string? SpreadsheetCopySha256 { get; private set; }
    public string? SpreadsheetPreparedJson { get; private set; }

    /// <summary>The reason of a rejection, the code of an unavailable spreadsheet or the last download failure.</summary>
    public string? SpreadsheetFailureCode { get; private set; }
    public int SpreadsheetFailureCount { get; private set; }
    public DateTime? SpreadsheetFirstFailureAtUtc { get; private set; }

    /// <summary>
    /// Fixes Meta's descriptor once. A descriptor that cannot be downloaded leaves the spreadsheet visible, with its caption and its
    /// name, but unavailable.
    /// </summary>
    public bool SetSpreadsheetDescriptor(string? mediaId, string? contentType, string? sha256, string? fileName)
    {
        if (Kind != WhatsAppMessageKind.Spreadsheet || Direction != WhatsAppMessageDirection.Inbound) return false;
        var name = SpreadsheetName(fileName);
        if (SpreadsheetState is not null)
            return SpreadsheetMediaId == mediaId && SpreadsheetContentType == contentType
                && SpreadsheetSha256 == sha256?.ToLowerInvariant() && SpreadsheetFileName == name;
        SpreadsheetFileName = name;
        if (mediaId is not { Length: > 0 and <= 128 } || !mediaId.All(char.IsAsciiDigit)
            || contentType is not { Length: > 0 and <= 128 } || contentType.Any(char.IsControl)
            || sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit))
        {
            SpreadsheetState = WhatsAppSpreadsheetState.Unavailable;
            return false;
        }

        SpreadsheetMediaId = mediaId;
        SpreadsheetContentType = contentType;
        SpreadsheetSha256 = sha256.ToLowerInvariant();
        SpreadsheetState = WhatsAppSpreadsheetState.Pending;
        return true;
    }

    /// <summary>Keeps the private copy and what its owner prepared once; storing the same copy again only clears the failures.</summary>
    public Result StoreSpreadsheet(string storageKey, long size, string sha256, string preparedJson)
    {
        if (SpreadsheetState == WhatsAppSpreadsheetState.Stored)
            return SpreadsheetStorageKey == storageKey && SpreadsheetSize == size && SpreadsheetCopySha256 == sha256
                && SpreadsheetPreparedJson == preparedJson ? Result.Success() : WhatsAppChatErrors.RequestConflict;
        if (SpreadsheetState != WhatsAppSpreadsheetState.Pending || string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500
            || !StorableText.IsStorable(storageKey) || size is <= 0 or > MaxSpreadsheetBytes
            || sha256 is not { Length: 64 } || !sha256.All(char.IsAsciiHexDigit)
            || string.IsNullOrWhiteSpace(preparedJson) || preparedJson.Length > MaxSpreadsheetPreparedLength || !StorableText.IsStorable(preparedJson))
            return FileErrors.InvalidMetadata;
        SpreadsheetStorageKey = storageKey;
        SpreadsheetSize = size;
        SpreadsheetCopySha256 = sha256;
        SpreadsheetPreparedJson = preparedJson;
        SpreadsheetState = WhatsAppSpreadsheetState.Stored;
        SpreadsheetFailureCount = 0;
        SpreadsheetFirstFailureAtUtc = null;
        SpreadsheetFailureCode = null;
        return Result.Success();
    }

    /// <summary>A pending spreadsheet that is not read keeps its reason; true only the first time, the one that is answered.</summary>
    public bool RejectSpreadsheet(string reason)
    {
        if (SpreadsheetState != WhatsAppSpreadsheetState.Pending || !IsFailureCode(reason)) return false;
        SpreadsheetState = WhatsAppSpreadsheetState.Rejected;
        SpreadsheetFailureCode = reason;
        return true;
    }

    /// <summary>A pending spreadsheet that will not be downloaded, with the code that says why.</summary>
    public void SetSpreadsheetUnavailable(string code)
    {
        if (SpreadsheetState != WhatsAppSpreadsheetState.Pending || !IsFailureCode(code)) return;
        SpreadsheetState = WhatsAppSpreadsheetState.Unavailable;
        SpreadsheetFailureCode = code;
    }

    /// <summary>
    /// A download failure that may pass: the count saturates at five, and the spreadsheet ends unavailable only when there are five
    /// and fifteen minutes passed since the first. True when it ends.
    /// </summary>
    public bool RegisterSpreadsheetFailure(DateTime nowUtc, string code)
    {
        if (SpreadsheetState != WhatsAppSpreadsheetState.Pending || nowUtc.Kind != DateTimeKind.Utc || !IsFailureCode(code)) return false;
        SpreadsheetFirstFailureAtUtc ??= nowUtc;
        SpreadsheetFailureCount = Math.Min(5, SpreadsheetFailureCount + 1);
        SpreadsheetFailureCode = code;
        if (SpreadsheetFailureCount < 5 || nowUtc - SpreadsheetFirstFailureAtUtc.Value < TimeSpan.FromMinutes(15)) return false;
        SpreadsheetState = WhatsAppSpreadsheetState.Unavailable;
        return true;
    }

    private static bool IsFailureCode(string? code) => code is { Length: > 0 and <= 128 } && StorableText.IsStorable(code);

    // The person's file name, without control characters and bounded; null if nothing is left.
    private static string? SpreadsheetName(string? fileName) => fileName is null ? null
        : StorableText.Clean(string.Concat(fileName.Where(character => !char.IsControl(character))).Trim(), MaxSpreadsheetFileNameLength);
}
