using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ClosedXML.Excel;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
/// <summary>ClosedXML (MIT) keeps typed cells and literal text behind the module's output port; no Office installation is required.</summary>
internal sealed class ClosedXmlFormWorkbookWriter : IFormWorkbookWriter
{
    private const string DefaultDate = "yyyy-mm-dd";
    private const string DefaultDateTime = "yyyy-mm-dd hh:mm:ss";
    // The width of a column comes from its header and its first rows, between these limits (in characters).
    private const int MeasuredRows = 200;
    private const double MinimumWidth = 8;
    private const double MaximumWidth = 60;
    // The header takes the tone of the sheet's header in the app and a group starts with the tone of its lines (XL1-A).
    private static readonly XLColor HeaderTone = XLColor.FromArgb(0xDF, 0xE8, 0xD9);
    private static readonly XLColor GroupLine = XLColor.FromArgb(0x87, 0x97, 0x86);

    public byte[] Write(FormWorkbookRequest request, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        foreach (var source in request.Sheets) Write(workbook.Worksheets.Add(source.Name), source, cancellationToken);
        using var stream = new MemoryStream();
        cancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return stream.ToArray();
    }

    private static void Write(IXLWorksheet sheet, FormWorkbookSheet request, CancellationToken cancellationToken)
    {
        for (var column = 0; column < request.Headers.Count; column++) sheet.Cell(1, column + 1).Value = request.Headers[column];
        for (var row = 0; row < request.Rows.Count; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var column = 0; column < request.Rows[row].Count; column++)
            {
                var source = request.Rows[row][column];
                if (source.Value is null) continue;
                var cell = sheet.Cell(row + 2, column + 1);
                cell.Value = XLCellValue.FromObject(source.Value, CultureInfo.InvariantCulture);
                if (source.Format is not null) cell.Style.NumberFormat.Format = source.Format;
                else if (source.Kind == "date") cell.Style.DateFormat.Format = request.DateFormat ?? DefaultDate;
                else if (source.Kind == "dateTime") cell.Style.DateFormat.Format = request.DateTimeFormat ?? DefaultDateTime;
                else if (source.Kind == "decimal") cell.Style.NumberFormat.Format = "0.############################";
            }
        }
        sheet.Row(1).Style.Font.Bold = true;
        if (request.Headers.Count > 0)
        {
            sheet.Range(1, 1, 1, request.Headers.Count).Style.Fill.BackgroundColor = HeaderTone;
            foreach (var start in request.GroupStarts ?? [])
            {
                if (start <= 0 || start >= request.Rows.Count) continue;
                var border = sheet.Range(start + 2, 1, start + 2, request.Headers.Count).Style.Border;
                border.TopBorder = XLBorderStyleValues.Thin;
                border.TopBorderColor = GroupLine;
            }
        }
        var widths = Widths(request);
        for (var column = 0; column < widths.Length; column++) sheet.Column(column + 1).Width = widths[column];
        sheet.SheetView.FreezeRows(1);
        if (request.Rows.Count > 0) sheet.Range(1, 1, request.Rows.Count + 1, request.Headers.Count).SetAutoFilter();
    }

    /// <summary>Each column as wide as its header, with room for the filter, or its longest value in the first rows.</summary>
    private static double[] Widths(FormWorkbookSheet request)
    {
        var widths = request.Headers.Select(header => Length(header) + 3d).ToArray();
        foreach (var row in request.Rows.Take(MeasuredRows))
            for (var column = 0; column < Math.Min(row.Count, widths.Length); column++)
                widths[column] = Math.Max(widths[column], Length(row[column], request));
        return widths.Select(width => Math.Clamp(width + 1, MinimumWidth, MaximumWidth)).ToArray();
    }

    private static int Length(FormWorkbookCell cell, FormWorkbookSheet sheet) => cell.Value switch
    {
        null => 0,
        string text => Length(text),
        DateTime => (cell.Format ?? (cell.Kind == "dateTime" ? sheet.DateTimeFormat ?? DefaultDateTime : sheet.DateFormat ?? DefaultDate)).Length,
        bool => 5,
        // Room for the thousands separators Excel shows.
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture).Length + 2,
        var value => value.ToString()?.Length ?? 0,
    };

    // A text with several lines is as wide as its longest line.
    private static int Length(string text) => text.Split('\n').Max(line => line.Length);
}
