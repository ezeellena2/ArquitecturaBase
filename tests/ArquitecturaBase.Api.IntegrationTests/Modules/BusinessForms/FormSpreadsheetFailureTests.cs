using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormSpreadsheetFailureTests
{
    [Theory]
    [InlineData("<f t=\"dataTable\" ref=\"A2:B3\"/>")]
    [InlineData("<f t=\"dataTable\" ref=\"A2:B3\" dt2D=\"1\" r1=\"A1\"/>")]
    public async Task A_data_table_missing_a_required_closedxml_attribute_returns_a_safe_file_error(string formula)
    {
        var workbook = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"),
            "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><row r=\"2\"><c r=\"A2\">" + formula + "<v>1</v></c></row></sheetData>");

        var result = await FormSpreadsheetFixture.ReadAsync(workbook);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.FileUnreadable", result.Error.Code);
    }
}