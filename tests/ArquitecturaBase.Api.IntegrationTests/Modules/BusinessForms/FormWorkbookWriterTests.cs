using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
using ClosedXML.Excel;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed class FormWorkbookWriterTests
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
}
