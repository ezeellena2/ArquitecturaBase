using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
using ClosedXML.Excel;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogExportWorkbookTests
{
    [Theory]
    [InlineData("es", BusinessFormCatalogValueType.Text)]
    [InlineData("en", BusinessFormCatalogValueType.Decimal)]
    public void A_template_contains_only_headers_and_the_recognizable_rules_row(string culture, BusinessFormCatalogValueType type)
    {
        var columns = FormCatalogImportTemplate.Columns(type, culture);
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(columns.Select(column => column.Header).ToArray(),
            [columns.Select(column => new FormWorkbookCell("text", column.Rule)).ToArray()], "Template"), Ct);
        using var stream = new MemoryStream(bytes); using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("", sheet.Cell(2, 1).GetString());
        Assert.Equal(columns.Select(column => column.Header), sheet.Row(1).Cells(1, columns.Count).Select(cell => cell.GetString()));
        Assert.Equal(columns.Select(column => column.Rule), sheet.Row(2).Cells(1, columns.Count).Select(cell => cell.GetString()));
        Assert.All(sheet.CellsUsed(), cell => Assert.False(cell.HasFormula));
    }

    [Theory]
    [InlineData("es", "123456789012345678")]
    [InlineData("en", "0.1234567890123456789012345678")]
    public void Catalog_round_trip_cells_are_exact_text_even_when_they_look_numeric_or_like_formulas(string culture, string canonical)
    {
        var columns = FormCatalogImportTemplate.Columns(BusinessFormCatalogValueType.Decimal, culture);
        string[] values = ["=SUM(A1)", "=2+2", "English", "Detail", "English detail", "Yes", canonical];
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(columns.Select(column => column.Header).ToArray(),
            [values.Select(value => new FormWorkbookCell("text", value)).ToArray()], "Options"), Ct);
        using var stream = new MemoryStream(bytes); using var workbook = new XLWorkbook(stream);
        var row = workbook.Worksheet(1).Row(2).Cells(1, values.Length).ToArray();
        Assert.All(row, cell => { Assert.Equal(XLDataType.Text, cell.DataType); Assert.False(cell.HasFormula); });
        Assert.Equal(values, row.Select(cell => cell.GetString()));
        Assert.Equal(canonical, row[^1].GetString());
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
