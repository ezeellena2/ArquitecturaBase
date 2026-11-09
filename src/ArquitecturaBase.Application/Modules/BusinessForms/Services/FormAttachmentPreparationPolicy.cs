using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// Stage 2 of the assistant authoring: the first sheet travels to the model with its counts and its first rows with content,
// within StructuredSpreadsheet.MaximumBytes. Every cell goes up to 80 characters and a cut text ends in «…». To fit, the
// sample loses rows from the last down to four (row 1 and the three the card shows), then its cells are cut by half, and
// only then it loses the rest of the rows, down to row 1. The names are bounded once and never cut with the sample. Each
// text is cut before measuring and each row is measured once for each length.
internal static class FormAttachmentPreparationPolicy
{
    private const int NameLength = 120;
    private const int SampleRows = 6;
    private const int CardRows = 4;
    private const int CellLength = 80;
    private const string Cut = "…";
    private const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvMediaType = "text/csv";

    // The person's file name without folders, bounded at the border of the port.
    public static string FileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return Shorten(Path.GetFileName(fileName.Replace('\\', '/')), NameLength);
    }

    public static StructuredAttachmentPreparation Prepare(string fileName, StructuredSpreadsheetKind kind, long size, FormSpreadsheet sheet)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(sheet);
        // A row has content as in the import: some text, or a cell the reader could not read (which has no text to show).
        var rows = sheet.Rows.Where(row => row.Cells.Any(HasContent)).ToList();
        if (rows.Count == 0) return StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Empty);
        var columns = rows.Max(row => row.Cells.Select((cell, index) => HasContent(cell) ? index + 1 : 0).Max());
        var prepared = new StructuredSpreadsheet(fileName, kind, kind == StructuredSpreadsheetKind.Xlsx ? XlsxMediaType : CsvMediaType, size,
            sheet.Sha256, Shorten(sheet.Name, NameLength), rows.Count, columns, []);
        var envelope = Bytes(prepared);
        var sample = rows.Take(SampleRows).Select(Sample).ToList();
        var sizes = sample.Select(Bytes).ToList();
        while (Total() > StructuredSpreadsheet.MaximumBytes && sample.Count > CardRows) Drop();
        for (var length = CellLength / 2; length > 0 && Total() > StructuredSpreadsheet.MaximumBytes; length /= 2)
        {
            sample = [.. sample.Select(row => Shorten(row, length))];
            sizes = [.. sample.Select(Bytes)];
        }
        while (Total() > StructuredSpreadsheet.MaximumBytes && sample.Count > 1) Drop();
        return StructuredAttachmentPreparation.Prepared(prepared with { Rows = sample });

        // The rows are serialized apart and joined with commas inside the envelope's empty array.
        int Total() => envelope + sizes.Sum() + Math.Max(0, sizes.Count - 1);

        void Drop()
        {
            sample.RemoveAt(sample.Count - 1);
            sizes.RemoveAt(sizes.Count - 1);
        }
    }

    private static bool HasContent(FormSpreadsheetCell cell) => cell.Error is not null || !string.IsNullOrWhiteSpace(cell.Text);

    private static StructuredSpreadsheetRow Sample(FormSpreadsheetRow row)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < row.Cells.Count; index++)
            if (row.Cells[index].Text is { } text && !string.IsNullOrWhiteSpace(text)) cells.Add(Column(index + 1), Shorten(text, CellLength));
        return new(row.Number, cells);
    }

    private static StructuredSpreadsheetRow Shorten(StructuredSpreadsheetRow row, int length) =>
        row with { Cells = row.Cells.ToDictionary(cell => cell.Key, cell => Shorten(cell.Value, length), StringComparer.Ordinal) };

    // 1 → A, 26 → Z, 27 → AA… 64 → BL.
    private static string Column(int number)
    {
        var letters = "";
        for (; number > 0; number = (number - 1) / 26) letters = (char)('A' + (number - 1) % 26) + letters;
        return letters;
    }

    private static string Shorten(string text, int length)
    {
        if (text.Length <= length) return text;
        var kept = length - Cut.Length;
        if (kept > 0 && char.IsHighSurrogate(text[kept - 1])) kept--;
        return text[..kept] + Cut;
    }

    private static int Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonSerializerOptions.Web).Length;
}
