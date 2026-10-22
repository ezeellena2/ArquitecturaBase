using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormSpreadsheetFormulaCacheTests
{
    [Theory]
    [InlineData("0.30000000000000004", "0.3")]
    [InlineData("9.999999999999998", "10")]
    [InlineData("1.2345678901234567e+20", "123456789012346000000")]
    [InlineData("1.2345678901234567e-20", "0.0000000000000000000123456789012346")]
    [InlineData("-0", "0")]
    public async Task Numeric_formula_caches_use_fifteen_significant_digits_without_recalculation(string cached, string expected)
    {
        var workbook = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"),
            "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Code</t></is></c></row><row r=\"2\"><c r=\"A2\"><f>999999+1</f><v>" + cached + "</v></c></row></sheetData>");

        var result = await FormSpreadsheetFixture.ReadAsync(workbook);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Rows[1].Cells[0].Text);
        Assert.True(result.Value.Rows[1].Cells[0].IsCanonicalValue);
    }

    [Fact]
    public async Task Exact_text_values_and_ordinary_numeric_cells_keep_their_existing_precision()
    {
        const string integer = "123456789012345678";
        const string number = "0.1234567890123456789012345678";
        var workbook = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"),
            "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Code</t></is></c></row><row r=\"2\"><c r=\"A2\" t=\"inlineStr\"><is><t>" + integer + "</t></is></c><c r=\"B2\" t=\"inlineStr\"><is><t>" + number + "</t></is></c><c r=\"C2\"><v>0.30000000000000004</v></c><c r=\"D2\" t=\"str\"><f>1+1</f><v>" + number + "</v></c></row></sheetData>");

        var result = await FormSpreadsheetFixture.ReadAsync(workbook);

        Assert.True(result.IsSuccess);
        Assert.Equal([integer, number, "0.30000000000000004", number], result.Value.Rows[1].Cells.Select(cell => cell.Text));
    }
}