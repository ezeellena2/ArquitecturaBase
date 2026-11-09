namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredSpreadsheetKind { Xlsx, Csv }

/// <summary>
/// Why an attachment was not prepared: <see cref="Unavailable"/> when no owner is installed, <see cref="Legacy"/> for an Excel
/// older than 2007 (.xls) and <see cref="Protected"/> for a file with a password.
/// </summary>
public enum StructuredAttachmentRejection { Unavailable, Legacy, Protected, MacroEnabled, TooLarge, Unreadable, Empty }

/// <summary>A row of the sample with its number in the sheet and its cells with text, by the letter of their column (A, B… BL).</summary>
public sealed record StructuredSpreadsheetRow(int Number, IReadOnlyDictionary<string, string> Cells)
{ public override string ToString() => "StructuredSpreadsheetRow { Content = [REDACTED] }"; }

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
    public override string ToString() => "StructuredSpreadsheet { Content = [REDACTED] }";
}

/// <summary>The prepared spreadsheet or why it was not prepared: never both.</summary>
public sealed record StructuredAttachmentPreparation
{
    private StructuredAttachmentPreparation() { }
    public StructuredSpreadsheet? Spreadsheet { get; private init; }
    public StructuredAttachmentRejection? Rejection { get; private init; }

    public static StructuredAttachmentPreparation Prepared(StructuredSpreadsheet spreadsheet)
    {
        ArgumentNullException.ThrowIfNull(spreadsheet);
        return new() { Spreadsheet = spreadsheet };
    }

    public static StructuredAttachmentPreparation Rejected(StructuredAttachmentRejection rejection) => new() { Rejection = rejection };

    public override string ToString() => Rejection is { } rejection
        ? $"StructuredAttachmentPreparation {{ Rejection = {rejection} }}" : "StructuredAttachmentPreparation { Content = [REDACTED] }";
}
