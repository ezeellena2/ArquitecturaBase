using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormSpreadsheetDiagnosticTests
{
    private const string SheetStart = "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_secondary_worksheet_limit_identifies_its_real_name(bool columns)
    {
        var workbook = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code", second: true);
        var rows = columns ? "<row><c r=\"BM1\"><v>1</v></c></row>"
            : FormSpreadsheetPreflightFixture.Rows(BusinessFormCatalog.MaxItems + 3, 1, content: true);
        workbook = FormSpreadsheetFixture.Sheet(workbook, SheetStart + rows + "</sheetData>", 2);

        var result = await FormSpreadsheetFixture.ReadAsync(workbook);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport." + (columns ? "TooManyColumns" : "TooManyRows"), result.Error.Code);
        Assert.NotNull(result.Error.Metadata);
        Assert.Equal("Ignored", result.Error.Metadata["sheet"]);
    }
}