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
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(["Text", "Empty", "Missing", "False", "Zero"],
            [new FormWorkbookCell[] { new("text", "=HYPERLINK(\"https://example.invalid\")"), new("text", null), new("text", "—"), new("boolean", false), new("decimal", 0m) }], "Responses"), TestContext.Current.CancellationToken);
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
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(["Integer", "Decimal", "Date"],
            [new FormWorkbookCell[] { new("integer", (long)integer), new("decimal", fraction), new("date", date) }], "Values"), TestContext.Current.CancellationToken);
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
}
