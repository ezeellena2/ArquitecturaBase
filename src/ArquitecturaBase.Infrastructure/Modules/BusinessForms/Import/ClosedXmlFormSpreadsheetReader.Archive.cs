using System.Globalization;
using System.IO.Compression;
using System.Xml;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

internal sealed partial class ClosedXmlFormSpreadsheetReader
{
    private const int MaximumExcelRows = 1048576;
    private const int MaximumExcelColumns = 16384;
    private static Result<Dictionary<int, int[]>> InspectArchive(byte[] bytes, CancellationToken ct)
    {
        using var content = new MemoryStream(bytes, writable: false);
        using var archive = new ZipArchive(content, ZipArchiveMode.Read);
        long declared = 0; long expanded = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var buffer = new byte[81920];
        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();
            if (!names.Add(entry.FullName)) return FormCatalogImportErrors.FileUnreadable;
            if (entry.Length > FormCatalogImportLimits.MaximumArchiveBytes - declared) return FormCatalogImportErrors.FileTooLarge;
            declared += entry.Length;
            using var source = entry.Open();
            int count;
            while ((count = source.Read(buffer, 0, (int)Math.Min(buffer.Length,
                FormCatalogImportLimits.MaximumArchiveBytes + 1L - expanded))) > 0)
            {
                ct.ThrowIfCancellationRequested();
                expanded += count;
                if (expanded > FormCatalogImportLimits.MaximumArchiveBytes) return FormCatalogImportErrors.FileTooLarge;
            }
        }
        if (archive.GetEntry("xl/sharedStrings.xml") is { } strings)
        {
            using var source = strings.Open();
            using var xml = XmlReader.Create(source, XmlSettings());
            while (xml.Read()) ct.ThrowIfCancellationRequested();
        }
        var (first, worksheets) = WorksheetPaths(archive);
        Dictionary<int, int[]>? firstRows = null;
        foreach (var path in worksheets)
        {
            var entry = archive.GetEntry(path) ?? throw new InvalidDataException("A worksheet part is missing.");
            var rows = InspectSheet(entry, path == first, ct);
            if (rows.IsFailure) return rows.Error;
            if (path == first) firstRows = rows.Value;
        }
        return firstRows is null ? FormCatalogImportErrors.FileUnreadable : Result.Success(firstRows);
    }

    private static (string First, HashSet<string> Worksheets) WorksheetPaths(ZipArchive archive)
    {
        var workbook = archive.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("The workbook part is missing.");
        string? firstId = null;
        using (var source = workbook.Open())
        using (var xml = XmlReader.Create(source, XmlSettings()))
            while (xml.Read())
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "sheet")
                {
                    firstId = xml.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                    break;
                }
        if (firstId is null) throw new InvalidDataException("The workbook has no first worksheet.");
        var relationships = archive.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("The workbook relationships are missing.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        string? first = null;
        using (var source = relationships.Open())
        using (var xml = XmlReader.Create(source, XmlSettings()))
            while (xml.Read())
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "Relationship"
                    && xml.GetAttribute("Type")?.EndsWith("/worksheet", StringComparison.Ordinal) == true)
                {
                    if (xml.GetAttribute("TargetMode") == "External") throw new InvalidDataException("A worksheet relationship is external.");
                    var target = xml.GetAttribute("Target") ?? throw new InvalidDataException("A worksheet target is missing.");
                    var uri = new Uri(new Uri("https://spreadsheet.invalid/xl/workbook.xml"), target);
                    if (uri.Scheme != "https" || uri.Host != "spreadsheet.invalid") throw new InvalidDataException("A worksheet target is outside the archive.");
                    var path = uri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
                    if (!path.StartsWith("xl/", StringComparison.Ordinal)) throw new InvalidDataException("A worksheet target is outside the workbook.");
                    paths.Add(path);
                    if (xml.GetAttribute("Id") == firstId) first = path;
                }
        return (first ?? throw new InvalidDataException("The first sheet is not a worksheet."), paths);
    }

    private static Result<Dictionary<int, int[]>> InspectSheet(ZipArchiveEntry entry, bool capture, CancellationToken ct)
    {
        var rows = new Dictionary<int, HashSet<int>>();
        var contentRows = new HashSet<int>();
        var currentRow = 0; var currentColumn = 0;
        using var source = entry.Open();
        using var xml = XmlReader.Create(source, XmlSettings());
        while (xml.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (xml.NodeType != XmlNodeType.Element) continue;
            if (xml.LocalName == "row")
            {
                if (xml.GetAttribute("r") is { } row)
                {
                    if (!int.TryParse(row, NumberStyles.None, CultureInfo.InvariantCulture, out currentRow)
                        || currentRow is <= 0 or > MaximumExcelRows)
                        throw new InvalidDataException("A worksheet row number is invalid.");
                }
                else if (currentRow == MaximumExcelRows) throw new InvalidDataException("A worksheet row number is invalid.");
                else currentRow++;
                currentColumn = 0;
            }
            else if (xml.LocalName == "c")
            {
                var address = xml.GetAttribute("r");
                var (number, column) = address is null ? (currentRow, currentColumn + 1) : CellAddress(address);
                currentColumn = column;
                if (number is <= 0 or > MaximumExcelRows || column is <= 0 or > MaximumExcelColumns)
                    throw new InvalidDataException("A worksheet cell address is outside the Excel grid.");
                var hasContent = false;
                using (var cell = xml.ReadSubtree())
                    while (cell.Read())
                        if (cell.NodeType == XmlNodeType.Element && cell.LocalName is "v" or "is" or "f") hasContent = true;
                if (!hasContent) continue;
                if (contentRows.Add(number) && contentRows.Count > MaximumRecords) return FormCatalogImportErrors.TooManyRows;
                if (column > FormCatalogImportLimits.MaximumColumns) return FormCatalogImportErrors.TooManyColumns;
                if (!capture) continue;
                if (!rows.TryGetValue(number, out var columns)) rows.Add(number, columns = []);
                if (!columns.Add(column)) throw new InvalidDataException("A worksheet cell is repeated.");
            }
        }
        if (capture && rows.Count > 0) rows.TryAdd(1, []);
        return rows.ToDictionary(row => row.Key, row => row.Value.Order().ToArray());
    }

    private static (int Row, int Column) CellAddress(string address)
    {
        var index = 0; var column = 0;
        while (index < address.Length && address[index] is >= 'A' and <= 'Z')
        {
            column = Math.Min(MaximumExcelColumns + 1, column * 26 + address[index] - 'A' + 1);
            index++;
        }
        if (index == 0 || !int.TryParse(address.AsSpan(index), NumberStyles.None, CultureInfo.InvariantCulture, out var row))
            throw new InvalidDataException("A worksheet cell address is invalid.");
        return (row, column);
    }

    private static XmlReaderSettings XmlSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
        MaxCharactersInDocument = FormCatalogImportLimits.MaximumArchiveBytes,
    };
}
