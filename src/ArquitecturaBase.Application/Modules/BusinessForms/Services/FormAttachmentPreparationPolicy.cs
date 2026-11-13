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
    private const int LoadingCellLength = 10;
    private const int HeaderCellLength = 20;
    private const string Cut = "…";
    private const string XlsxMediaType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string CsvMediaType = "text/csv";

    // The person's file name without folders, bounded at the border of the port.
    public static string FileName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return Shorten(Path.GetFileName(fileName.Replace('\\', '/')), NameLength);
    }

    // An OLE2 container with a password does not say whether it was a Word or an Excel: its extension names it, only for the text.
    public static StructuredAttachmentFileKind ProtectedKind(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        return Path.GetExtension(fileName.Replace('\\', '/')).ToUpperInvariant() switch
        {
            ".DOCX" or ".DOC" => StructuredAttachmentFileKind.Word,
            ".XLSX" or ".XLS" or ".CSV" => StructuredAttachmentFileKind.Spreadsheet,
            _ => StructuredAttachmentFileKind.Unknown,
        };
    }

    public static StructuredAttachmentPreparation Prepare(string fileName, StructuredSpreadsheetKind kind, long size, FormSpreadsheet sheet)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(sheet);
        // A row has content as in the import: some text, or a cell the reader could not read (which has no text to show).
        var rows = sheet.Rows.Where(FormSpreadsheetContentPolicy.HasContent).ToList();
        if (rows.Count == 0) return StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Spreadsheet);
        var columns = rows.Max(row => row.Cells.Select((cell, index) => FormSpreadsheetContentPolicy.HasContent(cell) ? index + 1 : 0).Max());
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

    // A sheet named as the samples name it, bounded once.
    public static string SheetName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Shorten(name, NameLength);
    }

    // VC1-A: a spreadsheet to load travels with the rows with content of the sheets with rows of data (FormDocumentRowPolicy.Sheets: never a
    // hidden one), each with its sheet, and how many rows of data it has, up to StructuredAttachmentLimits.MaximumLoadingRows; the name and
    // the counts stay the first sheet's, as in its own sample, for building or importing in the same turn. To fit, the cells of the rows of
    // data are cut by half down to ten characters and the headers' down to twenty, each only when that makes it shorter (a cut ends in «…»,
    // six bytes escaped); then the last rows are left out, with the headers of a sheet that keeps none: a row the model does not see stays
    // «Sin clasificar». Only headers that still do not fit alone go down to ten.
    public static StructuredAttachmentPreparation PrepareRows(string fileName, StructuredSpreadsheetKind kind, long size, IReadOnlyList<FormSpreadsheet> sheets)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(sheets);
        var read = FormDocumentRowPolicy.Sheets(sheets).Where(sheet => sheet.Rows.Count > 1).ToArray();
        var dataRows = read.Sum(sheet => sheet.Rows.Count - 1);
        if (dataRows == 0) return StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Spreadsheet);
        if (dataRows > StructuredAttachmentLimits.MaximumLoadingRows)
            return StructuredAttachmentPreparation.Rejected(StructuredAttachmentRejection.TooManyRows, StructuredAttachmentFileKind.Spreadsheet);
        var first = sheets[0].Rows.Where(FormSpreadsheetContentPolicy.HasContent).ToArray();
        var columns = first.Length == 0 ? 0
            : first.Max(row => row.Cells.Select((cell, index) => FormSpreadsheetContentPolicy.HasContent(cell) ? index + 1 : 0).Max());
        var prepared = new StructuredSpreadsheet(fileName, kind, kind == StructuredSpreadsheetKind.Xlsx ? XlsxMediaType : CsvMediaType, size,
            sheets[0].Sha256, SheetName(sheets[0].Name), first.Length, columns, []) { DataRowCount = dataRows };
        var rows = read.SelectMany(sheet => sheet.Rows.Select((row, index) => (Row: row, Sheet: sheet.Name, Header: index == 0))).ToArray();
        var envelope = Bytes(prepared);
        var sample = rows.Select(row => RowSample(row.Row, CellLength) with { Sheet = row.Sheet }).ToList();
        var sizes = sample.Select(Bytes).ToList();
        CutCells(headers: false, LoadingCellLength);
        CutCells(headers: true, HeaderCellLength);
        while (Total() > StructuredSpreadsheet.MaximumBytes && sample.Count > 1) Drop();
        while (sample.Count > 1 && rows[sample.Count - 1].Header) Drop();
        CutCells(headers: true, LoadingCellLength);
        return StructuredAttachmentPreparation.Prepared(prepared with { Rows = sample });

        int Total() => envelope + sizes.Sum() + Math.Max(0, sizes.Count - 1);

        void Drop()
        {
            sample.RemoveAt(sample.Count - 1);
            sizes.RemoveAt(sizes.Count - 1);
        }

        void CutCells(bool headers, int shortest)
        {
            for (var length = CellLength / 2; length >= shortest && Total() > StructuredSpreadsheet.MaximumBytes; length /= 2)
                for (var index = 0; index < sample.Count; index++)
                {
                    if (rows[index].Header != headers) continue;
                    var cut = RowSample(rows[index].Row, length);
                    sample[index] = sample[index] with
                    {
                        Cells = sample[index].Cells.ToDictionary(cell => cell.Key,
                            cell => Bytes(cut.Cells[cell.Key]) < Bytes(cell.Value) ? cut.Cells[cell.Key] : cell.Value, StringComparer.Ordinal),
                    };
                    sizes[index] = Bytes(sample[index]);
                }
        }
    }

    private static StructuredSpreadsheetRow Sample(FormSpreadsheetRow row) => RowSample(row, CellLength);

    private static StructuredSpreadsheetRow RowSample(FormSpreadsheetRow row, int length)
    {
        var cells = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < row.Cells.Count; index++)
            if (row.Cells[index].Text is { } text && !string.IsNullOrWhiteSpace(text)) cells.Add(Column(index + 1), Shorten(text, length));
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
