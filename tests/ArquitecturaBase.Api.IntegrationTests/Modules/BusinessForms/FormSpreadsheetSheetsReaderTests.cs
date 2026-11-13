using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// VC1-A, task 27: to load the rows of a spreadsheet the reader gives every worksheet, in the workbook's order, with its name and its rows,
// with the same limits and canonical cells as the first sheet the import and the assistant's authoring read. A real file of each case,
// built in memory, without a database.
public sealed class FormSpreadsheetSheetsReaderTests
{
    private const string SheetStart = "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_sheet_comes_in_the_workbook_order_with_its_name_its_rows_and_canonical_cells()
    {
        var bytes = Workbook(
            ("Enero", sheet =>
            {
                sheet.Cell(1, 1).Value = "Fecha"; sheet.Cell(1, 2).Value = "Cliente"; sheet.Cell(1, 3).Value = "Monto";
                sheet.Cell(2, 1).Value = new DateTime(2026, 11, 3); sheet.Cell(2, 2).Value = "Ferretería Norte"; sheet.Cell(2, 3).Value = 95000.5;
                sheet.Cell(4, 2).Value = "Librería Sur"; sheet.Cell(4, 3).Value = "12.500,00";
            }),
            ("Vacía", _ => { }),
            ("Febrero", sheet =>
            {
                sheet.Cell(3, 1).Value = "Fecha"; sheet.Cell(3, 2).Value = "Cliente";
                sheet.Cell(4, 1).Value = new DateTime(2026, 2, 1); sheet.Cell(4, 2).Value = "Kiosco Este";
            }));

        var sheets = await ReadAsync(bytes, FormSpreadsheetFormat.Xlsx);

        Assert.Equal(["Enero", "Vacía", "Febrero"], sheets.Select(sheet => sheet.Name));
        Assert.All(sheets, sheet => Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), sheet.Sha256));
        Assert.Equal([1, 2, 4], sheets[0].Rows.Select(row => row.Number));
        Assert.Equal(["2026-11-03", "Ferretería Norte", "95000.5"], sheets[0].Rows[1].Cells.Select(cell => cell.Text));
        Assert.Equal([null, "Librería Sur", "12.500,00"], sheets[0].Rows[2].Cells.Select(cell => cell.Text));
        Assert.Empty(sheets[1].Rows);
        // As the first sheet, a sheet whose content starts lower keeps an empty row 1: the rows with content are the caller's to tell.
        Assert.Equal([1, 3, 4], sheets[2].Rows.Select(row => row.Number));
        Assert.Equal(["2026-02-01", "Kiosco Este"], sheets[2].Rows[2].Cells.Select(cell => cell.Text));
    }

    // A list for the dropdowns of a template is usually a hidden sheet: the reader says so and the rows to load leave it out.
    [Fact]
    public async Task A_hidden_sheet_says_so()
    {
        var bytes = Workbook(
            ("Enero", sheet => { sheet.Cell(1, 1).Value = "Monto"; sheet.Cell(2, 1).Value = 100; }),
            ("Listas", sheet => { sheet.Cell(1, 1).Value = "Cliente"; sheet.Visibility = XLWorksheetVisibility.Hidden; }),
            ("Febrero", sheet => { sheet.Cell(1, 1).Value = "Monto"; sheet.Cell(2, 1).Value = 200; }));

        var sheets = await ReadAsync(bytes, FormSpreadsheetFormat.Xlsx);

        Assert.Equal([("Enero", false), ("Listas", true), ("Febrero", false)], sheets.Select(sheet => (sheet.Name, sheet.Hidden)));
        Assert.Equal("200", sheets[2].Rows[1].Cells[0].Text);
    }

    [Fact]
    public async Task A_csv_is_one_sheet_without_a_name()
    {
        var sheets = await ReadAsync(FormSpreadsheetFixture.Csv("Fecha;Monto\n03/11/2026;95.000,50\n"), FormSpreadsheetFormat.Csv);

        var sheet = Assert.Single(sheets);
        Assert.Equal("", sheet.Name);
        Assert.Equal([1, 2], sheet.Rows.Select(row => row.Number));
        Assert.Equal(["03/11/2026", "95.000,50"], sheet.Rows[1].Cells.Select(cell => cell.Text));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_limits_of_the_import_apply_to_every_sheet_and_name_the_one_that_breaks_them(bool columns)
    {
        var bytes = Workbook(("Enero", sheet => sheet.Cell(1, 1).Value = "Fecha"), ("Grande", sheet => sheet.Cell(1, 1).Value = "Fecha"));
        var oversized = columns ? "<row r=\"1\"><c r=\"BM1\"><v>1</v></c></row>"
            : string.Concat(Enumerable.Range(1, BusinessFormCatalog.MaxItems + 3).Select(number => "<row r=\"" + number + "\"><c r=\"A" + number + "\"><v>1</v></c></row>"));
        bytes = FormSpreadsheetFixture.Sheet(bytes, SheetStart + oversized + "</sheetData>", 2);

        var result = await ReadSheetsAsync(bytes, FormSpreadsheetFormat.Xlsx);

        Assert.Equal("Forms.CatalogImport." + (columns ? "TooManyColumns" : "TooManyRows"), result.Error.Code);
        Assert.Equal("Grande", result.Error.Metadata!["sheet"]);
    }

    [Fact]
    public async Task Only_a_workbook_or_a_csv_is_read_and_a_file_over_the_limit_is_not_opened()
    {
        Assert.Equal("Forms.CatalogImport.FileUnreadable", (await ReadSheetsAsync(FormSpreadsheetFixture.Compound("Workbook"), FormSpreadsheetFormat.Legacy)).Error.Code);
        Assert.Equal("Forms.CatalogImport.FileTooLarge",
            (await ReadSheetsAsync(new byte[FormCatalogImportLimits.MaximumFileBytes + 1], FormSpreadsheetFormat.Xlsx)).Error.Code);
    }

    private static async Task<IReadOnlyList<FormSpreadsheet>> ReadAsync(byte[] bytes, FormSpreadsheetFormat format)
    {
        var result = await ReadSheetsAsync(bytes, format);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        return result.Value;
    }

    // The reader as the module registers it.
    private static async Task<Result<IReadOnlyList<FormSpreadsheet>>> ReadSheetsAsync(byte[] bytes, FormSpreadsheetFormat format)
    {
        await using var services = new ServiceCollection().AddBusinessFormsInfrastructure().BuildServiceProvider();
        return services.GetRequiredService<IFormSpreadsheetReader>().ReadSheets(bytes, format, Ct);
    }

    private static byte[] Workbook(params (string Name, Action<IXLWorksheet> Configure)[] sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var (name, configure) in sheets) configure(workbook.AddWorksheet(name));
        using var content = new MemoryStream();
        workbook.SaveAs(content, new SaveOptions { EvaluateFormulasBeforeSaving = false });
        return content.ToArray();
    }
}
