using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ClosedXML.Excel;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormSpreadsheetPhysicalLimitsTests
{
    private const int MaximumPhysicalRows = BusinessFormCatalog.MaxItems + 2;
    private const int MaximumPhysicalCells = MaximumPhysicalRows * FormCatalogImportLimits.MaximumColumns;

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void Physical_row_count_includes_empty_rows_and_is_shared_by_all_worksheets(int sheets, bool content)
    {
        var workbook = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code", second: sheets == 2);
        var rows = FormSpreadsheetPreflightFixture.Rows(MaximumPhysicalRows / sheets + 1, content ? 1 : 0, content);
        for (var sheet = 1; sheet <= sheets; sheet++) workbook = FormSpreadsheetPreflightFixture.Sheet(workbook, rows, sheet);

        var result = FormSpreadsheetPreflightFixture.Inspect(workbook);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.TooManyRows", result.Error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Styled_empty_cells_spend_a_global_budget_before_closedxml_loads_any_sheet(int sheets)
    {
        const int columns = 81;
        var workbook = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code", second: sheets == 2);
        var rows = FormSpreadsheetPreflightFixture.Rows(MaximumPhysicalCells / sheets / columns + 1, columns);
        for (var sheet = 1; sheet <= sheets; sheet++) workbook = FormSpreadsheetPreflightFixture.Sheet(workbook, rows, sheet);

        var result = FormSpreadsheetPreflightFixture.Inspect(workbook);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.TooManyCells", result.Error.Code);
    }

    [Theory]
    [InlineData("A1:XFD1048576")]
    [InlineData("A1:BM10000")]
    public void Array_formula_expansion_is_bounded_without_constructing_a_closedxml_workbook(string range)
    {
        var workbook = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code");
        workbook = FormSpreadsheetPreflightFixture.Sheet(workbook,
            "<row><c r=\"A1\"><f t=\"array\" ref=\"" + range + "\">1</f><v>1</v></c></row>");

        // Invoke the existing preflight directly: a regression must never load the enormous array in ClosedXML.
        var result = FormSpreadsheetPreflightFixture.Inspect(workbook);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.TooManyCells", result.Error.Code);
    }

    [Fact]
    public void Array_formula_ranges_share_the_physical_cell_budget_between_worksheets()
    {
        var workbook = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code", second: true);
        const string rows = "<row><c r=\"A1\"><f t=\"array\" ref=\"A1:AH10000\">1</f><v>1</v></c></row>";
        workbook = FormSpreadsheetPreflightFixture.Sheet(workbook, rows);
        workbook = FormSpreadsheetPreflightFixture.Sheet(workbook, rows, 2);

        var result = FormSpreadsheetPreflightFixture.Inspect(workbook);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.TooManyCells", result.Error.Code);
    }

    [Theory]
    [InlineData("A1:B2")]
    [InlineData("$A$1:$B$2")]
    public async Task Array_formula_ranges_keep_the_same_address_constraints_as_closedxml(string range)
    {
        var workbook = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"),
            "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><row><c r=\"A1\"><f t=\"array\" ref=\"" + range + "\">1</f><v>1</v></c></row></sheetData>");
        using var content = new MemoryStream(workbook);
        if (range.StartsWith('$'))
        {
            Assert.Throws<FormatException>(() => new XLWorkbook(content, new LoadOptions { RecalculateAllFormulas = false }));
            var unreadable = await FormSpreadsheetFixture.ReadAsync(workbook);
            Assert.Equal("Forms.CatalogImport.FileUnreadable", unreadable.Error.Code);
        }
        else
        {
            using var closedXml = new XLWorkbook(content, new LoadOptions { RecalculateAllFormulas = false });
            Assert.Equal(1d, closedXml.Worksheet(1).Cell(1, 1).CachedValue.GetNumber());
            var result = await FormSpreadsheetFixture.ReadAsync(workbook);
            Assert.True(result.IsSuccess);
            Assert.Equal("1", result.Value.Rows[0].Cells[0].Text);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Aliased_worksheet_parts_spend_the_budget_for_each_sheet_closedxml_loads(bool sameRelationship)
    {
        var workbook = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code", second: true);
        var alias = FormSpreadsheetPreflightFixture.AliasSecondSheet(workbook, sameRelationship);
        using (var content = new MemoryStream(alias))
        using (var closedXml = new XLWorkbook(content, new LoadOptions { RecalculateAllFormulas = false }))
        {
            Assert.Equal(2, closedXml.Worksheets.Count);
            Assert.Equal("Code", closedXml.Worksheet(2).Cell(1, 1).CachedValue.GetText());
        }
        workbook = FormSpreadsheetPreflightFixture.Sheet(workbook,
            FormSpreadsheetPreflightFixture.Rows(MaximumPhysicalRows / 2 + 1, 0));
        alias = FormSpreadsheetPreflightFixture.AliasSecondSheet(workbook, sameRelationship);

        var result = FormSpreadsheetPreflightFixture.Inspect(alias);

        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.TooManyRows", result.Error.Code);
    }

    [Fact]
    public void The_exact_physical_limits_are_allowed_without_expanding_the_returned_grid()
    {
        var workbook = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code");
        workbook = FormSpreadsheetPreflightFixture.Sheet(workbook,
            FormSpreadsheetPreflightFixture.Rows(MaximumPhysicalRows, FormCatalogImportLimits.MaximumColumns));

        var result = FormSpreadsheetPreflightFixture.Inspect(workbook);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}
