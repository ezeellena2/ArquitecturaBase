using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Import;

internal sealed partial class ClosedXmlFormSpreadsheetReader : IFormSpreadsheetReader
{
    // The policy distinguishes a template legend from data and enforces exactly MaxItems data rows.
    private const int MaximumRecords = BusinessFormCatalog.MaxItems + 2;

    public async Task<Result<FormSpreadsheet>> ReadAsync(Stream content, string fileName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var source = await ReadBoundedAsync(content, cancellationToken);
            if (source.IsFailure) return source.Error;
            var bytes = source.Value;
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (Path.GetExtension(fileName).Equals(".csv", StringComparison.OrdinalIgnoreCase))
                return FormCsvReader.Read(bytes, Path.GetFileNameWithoutExtension(fileName.Replace('\\', '/')), sha, cancellationToken);
            if (!Path.GetExtension(fileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                || bytes.Length < 4 || bytes[0] != 'P' || bytes[1] != 'K' || bytes[2] != 3 || bytes[3] != 4)
                return FormCatalogImportErrors.FileUnreadable;
            return ReadWorkbook(bytes, sha, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or XmlException or FormatException
            or ArgumentException or NotSupportedException or OpenXmlPackageException)
        {
            return FormCatalogImportErrors.FileUnreadable;
        }
    }

    private static async Task<Result<byte[]>> ReadBoundedAsync(Stream content, CancellationToken ct)
    {
        if (content.CanSeek && content.Length - content.Position > FormCatalogImportLimits.MaximumFileBytes)
            return FormCatalogImportErrors.FileTooLarge;
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var count = await content.ReadAsync(buffer.AsMemory(0,
                Math.Min(buffer.Length, FormCatalogImportLimits.MaximumFileBytes + 1 - (int)copy.Length)), ct);
            if (count == 0) return copy.ToArray();
            if (copy.Length + count > FormCatalogImportLimits.MaximumFileBytes) return FormCatalogImportErrors.FileTooLarge;
            copy.Write(buffer, 0, count);
        }
    }

    private static Result<FormSpreadsheet> ReadWorkbook(byte[] bytes, string sha, CancellationToken ct)
    {
        var layout = InspectArchive(bytes, ct);
        if (layout.IsFailure) return layout.Error;
        using var content = new MemoryStream(bytes, writable: false);
        using var workbook = new XLWorkbook(content, new LoadOptions { RecalculateAllFormulas = false });
        if (workbook.Worksheets.Count == 0) return FormCatalogImportErrors.FileUnreadable;
        var sheet = workbook.Worksheet(1);
        var rows = new List<FormSpreadsheetRow>(layout.Value.Count);
        var width = layout.Value.Count == 0 ? 0 : layout.Value.Values.SelectMany(columns => columns).Max();
        foreach (var (number, _) in layout.Value.OrderBy(row => row.Key))
        {
            ct.ThrowIfCancellationRequested();
            var cells = new FormSpreadsheetCell[width];
            for (var column = 1; column <= width; column++) cells[column - 1] = ReadCell(sheet.Cell(number, column));
            rows.Add(new(number, cells));
        }
        return new FormSpreadsheet(sheet.Name, sha, rows);
    }

    private static FormSpreadsheetCell ReadCell(IXLCell cell)
    {
        var value = cell.CachedValue;
        if (cell.HasFormula && value.Type == XLDataType.Blank) return new(null, FormCatalogImportErrors.FormulaValueMissing);
        return value.Type switch
        {
            XLDataType.Blank => new(null),
            XLDataType.Text => new(value.GetText()),
            XLDataType.Number => new(CanonicalNumber(value.GetNumber()), IsCanonicalValue: true),
            XLDataType.Boolean => new(value.GetBoolean() ? "true" : "false"),
            XLDataType.DateTime => new(value.GetDateTime().ToString(value.GetDateTime().TimeOfDay == TimeSpan.Zero
                ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture), IsCanonicalValue: true),
            _ => new(null, FormCatalogImportErrors.CellUnreadable),
        };
    }

    private static string CanonicalNumber(double value)
    {
        if (value == 0) return "0";
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentAt = text.IndexOf('E');
        if (exponentAt < 0) return text;
        var negative = text[0] == '-';
        var mantissa = text.AsSpan(negative ? 1 : 0, exponentAt - (negative ? 1 : 0));
        var point = mantissa.IndexOf('.');
        var integerLength = point < 0 ? mantissa.Length : point;
        var digits = mantissa.ToString().Replace(".", "", StringComparison.Ordinal);
        var position = integerLength + int.Parse(text.AsSpan(exponentAt + 1), CultureInfo.InvariantCulture);
        var expanded = position <= 0 ? "0." + new string('0', -position) + digits
            : position >= digits.Length ? digits + new string('0', position - digits.Length)
            : digits.Insert(position, ".");
        return negative ? "-" + expanded : expanded;
    }
}
