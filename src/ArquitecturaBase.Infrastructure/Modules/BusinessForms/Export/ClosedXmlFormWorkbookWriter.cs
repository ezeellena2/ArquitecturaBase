using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ClosedXML.Excel;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
/// <summary>ClosedXML (MIT) keeps typed cells and literal text behind the module's output port; no Office installation is required.</summary>
internal sealed class ClosedXmlFormWorkbookWriter : IFormWorkbookWriter
{
    public byte[] Write(FormWorkbookRequest request, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(request.SheetName);
        for (var column = 0; column < request.Headers.Count; column++) sheet.Cell(1, column + 1).Value = request.Headers[column];
        for (var row = 0; row < request.Rows.Count; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var column = 0; column < request.Rows[row].Count; column++)
            {
                var source = request.Rows[row][column];
                if (source.Value is null) continue;
                var cell = sheet.Cell(row + 2, column + 1);
                cell.Value = XLCellValue.FromObject(source.Value, System.Globalization.CultureInfo.InvariantCulture);
                if (source.Kind == "date") cell.Style.DateFormat.Format = "yyyy-mm-dd";
                else if (source.Kind == "dateTime") cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
                else if (source.Kind == "decimal") cell.Style.NumberFormat.Format = "0.############################";
            }
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns(1, request.Headers.Count).Width = 26;
        sheet.SheetView.FreezeRows(1);
        if (request.Rows.Count > 0) sheet.Range(1, 1, request.Rows.Count + 1, request.Headers.Count).SetAutoFilter();
        using var stream = new MemoryStream();
        cancellationToken.ThrowIfCancellationRequested();
        workbook.SaveAs(stream);
        cancellationToken.ThrowIfCancellationRequested();
        return stream.ToArray();
    }
}
