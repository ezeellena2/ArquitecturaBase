using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredSpreadsheetKind { Xlsx, Csv }

/// <summary>
/// Why an attachment was not prepared: <see cref="Unavailable"/> when no owner is installed, <see cref="Legacy"/> for an Excel or a
/// Word older than 2007 (.xls, .doc), <see cref="Protected"/> for a file with a password, <see cref="TooManyPages"/> for a PDF over
/// <see cref="StructuredAttachmentLimits.MaximumPages"/> and <see cref="TooManyRows"/> for a spreadsheet to load over
/// <see cref="StructuredAttachmentLimits.MaximumLoadingRows"/>.
/// </summary>
public enum StructuredAttachmentRejection { Unavailable, Legacy, Protected, MacroEnabled, TooLarge, Unreadable, Empty, TooManyPages, TooManyRows }

/// <summary>What a rejected file was, for the text to name it: a spreadsheet, a PDF, a photo, a Word or something else.</summary>
public enum StructuredAttachmentFileKind { Spreadsheet, Pdf, Image, Word, Unknown }

/// <summary>
/// The limits of a file attached to the assistant, each in one place: any kind up to <see cref="MaximumBytes"/>, a PDF up to
/// <see cref="MaximumPages"/> pages, a photo up to <see cref="MaximumImageSide"/> pixels per side and <see cref="MaximumImagePixels"/>
/// in all (those of the photos of the forms), the text of a Word up to <see cref="MaximumDocumentCharacters"/>, and a spreadsheet to load
/// up to <see cref="MaximumLoadingRows"/> rows of data in all its sheets, without their headers (VC1-A; building or importing keep theirs).
/// </summary>
public static class StructuredAttachmentLimits
{
    public const int MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumPages = 10;
    public const int MaximumImageSide = 16_384;
    public const long MaximumImagePixels = 32L * 1024 * 1024;
    public const int MaximumDocumentCharacters = 8_000;
    public const int MaximumLoadingRows = 50;
}

/// <summary>What the named account may attach: spreadsheets, and documents (photos, PDF and Word), each decided by its owner.</summary>
public sealed record StructuredAttachmentAccess(bool Spreadsheets, bool Documents)
{
    public static StructuredAttachmentAccess None { get; } = new(false, false);
}

/// <summary>A row of the sample with its number in the sheet and its cells with text, by the letter of their column (A, B… BL).</summary>
public sealed record StructuredSpreadsheetRow(int Number, IReadOnlyDictionary<string, string> Cells)
{
    /// <summary>In a sample to load (VC1-A), the sheet of the row: its name, empty in a CSV; null in the first sheet's sample.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sheet { get; init; }

    public override string ToString() => "StructuredSpreadsheetRow { Content = [REDACTED] }";
}

/// <summary>
/// The first sheet of a spreadsheet prepared for the assistant: the file (name, kind, media type, size and SHA-256 of the
/// original bytes), the name of the sheet (empty in a CSV), how many rows have content (row 1 included) and how many columns,
/// and the first rows with content as a sample. Serialized with the web defaults it takes at most <see cref="MaximumBytes"/>;
/// a cut text ends in «…».
/// </summary>
public sealed record StructuredSpreadsheet(string FileName, StructuredSpreadsheetKind Kind, string MediaType, long Size, string Sha256,
    string SheetName, int RowCount, int ColumnCount, IReadOnlyList<StructuredSpreadsheetRow> Rows)
{
    public const int MaximumBytes = 12 * 1024;

    /// <summary>
    /// A sample to load (VC1-A): the rows of data of every sheet, without each one's headers (its first row with content); its rows are
    /// those of every sheet, each with its <see cref="StructuredSpreadsheetRow.Sheet"/>, while the name and the counts stay the first
    /// sheet's. Null in the first sheet's sample, the one to build or import.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DataRowCount { get; init; }

    public override string ToString() => "StructuredSpreadsheet { Content = [REDACTED] }";
}

/// <summary>The prepared spreadsheet or document, or why the file was not prepared and what it was: exactly one of the three.</summary>
public sealed record StructuredAttachmentPreparation
{
    private StructuredAttachmentPreparation() { }
    public StructuredSpreadsheet? Spreadsheet { get; private init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructuredDocument? Document { get; private init; }

    public StructuredAttachmentRejection? Rejection { get; private init; }

    /// <summary>What the rejected file was; null when something was prepared.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StructuredAttachmentFileKind? FileKind { get; private init; }

    public static StructuredAttachmentPreparation Prepared(StructuredSpreadsheet spreadsheet)
    {
        ArgumentNullException.ThrowIfNull(spreadsheet);
        return new() { Spreadsheet = spreadsheet };
    }

    public static StructuredAttachmentPreparation Prepared(StructuredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new() { Document = document };
    }

    public static StructuredAttachmentPreparation Rejected(StructuredAttachmentRejection rejection,
        StructuredAttachmentFileKind fileKind = StructuredAttachmentFileKind.Unknown) => new() { Rejection = rejection, FileKind = fileKind };

    public override string ToString() => Rejection is { } rejection
        ? $"StructuredAttachmentPreparation {{ Rejection = {rejection}, FileKind = {FileKind} }}"
        : "StructuredAttachmentPreparation { Content = [REDACTED] }";
}
