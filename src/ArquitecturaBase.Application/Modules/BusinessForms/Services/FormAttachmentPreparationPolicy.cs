using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// Stage 2 of the assistant authoring: the first sheet travels to the model with its counts and its first rows with content,
// within StructuredSpreadsheet.MaximumBytes. To fit, row 1 and every column always stay: first the sample loses rows from the
// last, then the texts (cells and names) are cut to 80 characters with «…»; if 64 wide columns still do not fit, they are
// cut by half again, so the cap always holds.
internal static class FormAttachmentPreparationPolicy
{
    private const int SampleRows = 6;
    private const int CutLength = 80;
    private const string Cut = "…";
    private const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvMediaType = "text/csv";

    public static StructuredAttachmentPreparation Prepare(string fileName, StructuredSpreadsheetKind kind, long size, FormSpreadsheet sheet)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        // A row has content as in the import: some text, or a cell the reader could not read (which has no text to show).
        var rows = sheet.Rows.Where(row => row.Cells.Any(HasContent)).ToList();
        if (rows.Count == 0) return StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Empty);
        var columns = rows.Max(row => row.Cells.Select((cell, index) => HasContent(cell) ? index + 1 : 0).Max());
        var prepared = new StructuredSpreadsheet(fileName, kind, kind == StructuredSpreadsheetKind.Xlsx ? XlsxMediaType : CsvMediaType, size,
            sheet.Sha256, sheet.Name, rows.Count, columns, [.. rows.Take(SampleRows).Select(Sample)]);
        while (Bytes(prepared) > StructuredSpreadsheet.MaximumBytes && prepared.Rows.Count > 1)
            prepared = prepared with { Rows = [.. prepared.Rows.Take(prepared.Rows.Count - 1)] };
        for (var length = CutLength; length > 0 && Bytes(prepared) > StructuredSpreadsheet.MaximumBytes; length /= 2)
            prepared = Shorten(prepared, length);
        return StructuredAttachmentPreparation.Prepared(prepared);
    }

    private static bool HasContent(FormSpreadsheetCell cell) => cell.Error is not null || !string.IsNullOrWhiteSpace(cell.Text);

    private static StructuredSpreadsheetRow Sample(FormSpreadsheetRow row) => new(row.Number,
        [.. row.Cells.Select((cell, index) => (cell.Text, index)).Where(cell => !string.IsNullOrWhiteSpace(cell.Text))
            .Select(cell => new StructuredSpreadsheetCell(Column(cell.index + 1), cell.Text!))]);

    // 1 → A, 26 → Z, 27 → AA… 64 → BL.
    private static string Column(int number)
    {
        var letters = "";
        for (; number > 0; number = (number - 1) / 26) letters = (char)('A' + (number - 1) % 26) + letters;
        return letters;
    }

    private static StructuredSpreadsheet Shorten(StructuredSpreadsheet prepared, int length) => prepared with
    {
        FileName = Shorten(prepared.FileName, length),
        SheetName = Shorten(prepared.SheetName, length),
        Rows = [.. prepared.Rows.Select(row => row with { Cells = [.. row.Cells.Select(cell => cell with { Text = Shorten(cell.Text, length) })] })],
    };

    private static string Shorten(string text, int length)
    {
        if (text.Length <= length) return text;
        var kept = length - Cut.Length;
        if (kept > 0 && char.IsHighSurrogate(text[kept - 1])) kept--;
        return text[..kept] + Cut;
    }

    private static int Bytes(StructuredSpreadsheet prepared) => JsonSerializer.SerializeToUtf8Bytes(prepared, FormDraftPolicy.JsonOptions).Length;
}
