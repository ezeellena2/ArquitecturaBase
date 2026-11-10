using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
using ClosedXML.Excel;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed class FormWorkbookWriterTests(ITestOutputHelper output)
{
    [Fact]
    public void Workbook_keeps_text_literal_and_never_converts_a_supplied_formula_to_an_executable_formula()
    {
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new([new(["Text", "Empty", "Missing", "False", "Zero"],
            [new FormWorkbookCell[] { new("text", "=HYPERLINK(\"https://example.invalid\")"), new("text", null), new("text", "—"), new("boolean", false), new("decimal", 0m) }], "Responses")]), TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.False(sheet.Cell(2, 1).HasFormula);
        Assert.Equal("=HYPERLINK(\"https://example.invalid\")", sheet.Cell(2, 1).GetString());
        Assert.True(sheet.Cell(2, 2).IsEmpty());
        Assert.Equal("—", sheet.Cell(2, 3).GetString());
        Assert.False(sheet.Cell(2, 4).GetBoolean());
        Assert.Equal(0d, sheet.Cell(2, 5).GetDouble());
    }
    [Fact]
    public void Workbook_catalog_values_are_numeric_or_dates_and_eighteen_digits_have_Excel_number_precision()
    {
        const decimal integer = 123456789012345678m;
        const decimal fraction = 0.1234567890123456789012345678m;
        var date = new DateTime(2026, 10, 8);
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new([new(["Integer", "Decimal", "Date"],
            [new FormWorkbookCell[] { new("integer", (long)integer), new("decimal", fraction), new("date", date) }], "Values")]), TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        var number = sheet.Cell(2, 1).CachedValue.GetNumber();
        output.WriteLine("Captured canonical integer: " + integer.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("XLSX numeric value: " + number.ToString("R", CultureInfo.InvariantCulture));
        output.WriteLine("Captured canonical decimal: " + fraction.ToString(CultureInfo.InvariantCulture));
        output.WriteLine("XLSX numeric decimal: " + sheet.Cell(2, 2).CachedValue.GetNumber().ToString("R", CultureInfo.InvariantCulture));
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 1).DataType);
        Assert.Equal(123456789012346000d, number);
        Assert.NotEqual(integer.ToString(CultureInfo.InvariantCulture), number.ToString("0", CultureInfo.InvariantCulture));
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 2).DataType);
        Assert.Equal(0.123456789012346d, sheet.Cell(2, 2).CachedValue.GetNumber());
        Assert.Equal(XLDataType.DateTime, sheet.Cell(2, 3).DataType);
        Assert.Equal(date, sheet.Cell(2, 3).GetDateTime());

    }
    [Fact]
    public void Workbook_cells_keep_their_own_number_format()
    {
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new([new(["Integer", "Decimal", "Whole", "Date", "Plain"],
            [new FormWorkbookCell[] { new("integer", 1234567L, "#,##0"), new("decimal", 1234.5m, "#,##0.0"), new("decimal", 1500m, "#,##0"),
                new("date", new DateTime(2026, 10, 22), "dd/mm/yyyy"), new("decimal", 0.5m) }], "Options")]), TestContext.Current.CancellationToken);
        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var cells = workbook.Worksheet(1).Row(2).Cells(1, 5).ToArray();
        Assert.Equal(["#,##0", "#,##0.0", "#,##0", "dd/mm/yyyy", "0.############################"], cells.Select(cell => cell.Style.NumberFormat.Format));
        Assert.Equal([XLDataType.Number, XLDataType.Number, XLDataType.Number, XLDataType.DateTime, XLDataType.Number], cells.Select(cell => cell.DataType));
        // Excel shows the separators of whoever opens it; a whole decimal has no trailing comma.
        var spanish = CultureInfo.GetCultureInfo("es-AR");
        Assert.Equal(["1.234.567", "1.234,5", "1.500", "22/10/2026"], cells.Take(4).Select(cell => cell.GetFormattedString(spanish)));
        Assert.Equal(["1,234,567", "1,234.5", "1,500"], cells.Take(3).Select(cell => cell.GetFormattedString(CultureInfo.GetCultureInfo("en-US"))));
    }
    [Fact]
    public void Several_sheets_keep_their_order_names_bold_headers_and_typed_cells()
    {
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new([
            new(["Número", "Artículos"], [new FormWorkbookCell[] { new("text", "T-000001"), new("integer", 2L) }], "Respuestas"),
            new(["Número", "Ítem", "Cantidad"], [new FormWorkbookCell[] { new("text", "T-000001"), new("integer", 1L), new("decimal", 200.5m) }], "Artículos")]),
            TestContext.Current.CancellationToken);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        Assert.Equal(["Respuestas", "Artículos"], workbook.Worksheets.Select(sheet => sheet.Name));
        var items = workbook.Worksheet("Artículos");
        Assert.Equal("Ítem", items.Cell(1, 2).GetString());
        Assert.True(items.Cell(1, 2).Style.Font.Bold);
        Assert.Equal(XLDataType.Number, items.Cell(2, 2).DataType);
        Assert.Equal(200.5m, items.Cell(2, 3).GetValue<decimal>());
        Assert.Equal(2, workbook.Worksheet("Respuestas").Cell(2, 2).GetValue<int>());
    }
    [Fact]
    public void The_header_takes_the_tone_of_the_sheet_and_each_column_fits_what_it_holds_within_a_limit()
    {
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new([new(["N°", "Cargó", "Observación"],
            [new FormWorkbookCell[] { new("text", "T-000001"), new("text", "Ezequiel Ellena Hotmail - User"), new("text", new string('x', 300)) }], "Respuestas")]),
            TestContext.Current.CancellationToken);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        Assert.All(sheet.Row(1).Cells(1, 3), cell =>
        {
            Assert.True(cell.Style.Font.Bold);
            Assert.Equal(XLColor.FromArgb(0xDF, 0xE8, 0xD9), cell.Style.Fill.BackgroundColor);
        });
        // A short number is narrower than a name, which fits whole; a long text stops at the limit and stays in its cell.
        Assert.True(sheet.Column(1).Width < sheet.Column(2).Width);
        Assert.True(sheet.Column(2).Width >= "Ezequiel Ellena Hotmail - User".Length);
        Assert.InRange(sheet.Column(3).Width, 59, 61);
        Assert.Equal(new string('x', 300), sheet.Cell(2, 3).GetString());
        Assert.Equal(1, sheet.SheetView.SplitRow);
        Assert.NotNull(sheet.AutoFilter.Range);
    }
    [Fact]
    public void Each_group_starts_with_a_line_and_dates_take_the_formats_of_the_sheet()
    {
        var received = new DateTime(2026, 11, 9, 15, 42, 0);
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new([new(["N°", "Recibida", "Vence", "Ítem"],
            [
                new FormWorkbookCell[] { new("text", "RMP-000002"), new("dateTime", received), new("date", new DateTime(2026, 11, 30)), new("integer", 1L) },
                new FormWorkbookCell[] { new("text", "RMP-000002"), new("dateTime", received), new("date", new DateTime(2026, 11, 30)), new("integer", 2L) },
                new FormWorkbookCell[] { new("text", "RMP-000001"), new("dateTime", received.AddMinutes(-14)), new("date", new DateTime(2026, 11, 30), "yyyy"), new("integer", 1L) },
            ], "Respuestas", GroupStarts: [0, 2], DateFormat: "dd/mm/yyyy", DateTimeFormat: "dd/mm/yyyy hh:mm")]),
            TestContext.Current.CancellationToken);
        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        // The second response starts on row 4; the second item of the first one keeps the usual grid.
        Assert.All(sheet.Row(4).Cells(1, 4), cell => Assert.Equal(XLBorderStyleValues.Thin, cell.Style.Border.TopBorder));
        Assert.All(sheet.Row(3).Cells(1, 4), cell => Assert.Equal(XLBorderStyleValues.None, cell.Style.Border.TopBorder));
        Assert.Equal("dd/mm/yyyy hh:mm", sheet.Cell(2, 2).Style.DateFormat.Format);
        Assert.Equal("dd/mm/yyyy", sheet.Cell(2, 3).Style.DateFormat.Format);
        // A cell with its own format keeps it.
        Assert.Equal("yyyy", sheet.Cell(4, 3).Style.NumberFormat.Format);
        Assert.Equal("09/11/2026 15:42", sheet.Cell(2, 2).GetFormattedString(CultureInfo.GetCultureInfo("es-AR")));
    }
}
