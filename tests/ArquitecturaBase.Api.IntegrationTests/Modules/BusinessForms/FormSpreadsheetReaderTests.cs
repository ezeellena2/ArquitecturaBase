using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormSpreadsheetReaderTests(ITestOutputHelper output)
{
    private const string SheetStart = "<sheetData xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">";

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    [InlineData("\t")]
    public async Task Csv_preserves_quotes_multiline_fields_and_the_hash_of_original_utf8_bytes(string delimiter)
    {
        var bytes = FormSpreadsheetFixture.Csv("Code" + delimiter + "Text\r\nA" + delimiter + "\"North, south; tab\t and \"\"quoted\"\"\r\nnext line\"\r\n", bom: true);
        using var content = new MemoryStream(bytes);

        var result = await FormSpreadsheetFixture.ReadAsync(content, "folder/options.csv");

        Assert.True(result.IsSuccess);
        Assert.Equal("options", result.Value.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Value.Sha256);
        Assert.Equal([1, 2], result.Value.Rows.Select(row => row.Number));
        Assert.Equal("North, south; tab\t and \"quoted\"\r\nnext line", result.Value.Rows[1].Cells[1].Text);
        Assert.All(result.Value.Rows.SelectMany(row => row.Cells), cell => Assert.Null(cell.Error));
        Assert.True(content.CanRead);
    }

    [Theory]
    [InlineData("Code,Text\nA,\"unfinished")]
    [InlineData("Code,Text\nA,\"closed\"junk")]
    [InlineData("Code,Text\nA,un\"quoted")]
    public async Task Malformed_csv_quotes_return_a_safe_file_error(string text)
    {
        var result = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv(text), "options.csv");
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.FileUnreadable", result.Error.Code);
        Assert.DoesNotContain("unfinished", result.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Csv_requires_strict_utf8_and_csv_extension_even_for_tab_separated_data()
    {
        var invalid = await FormSpreadsheetFixture.ReadAsync([0x43, 0xF1, 0x0A], "options.csv");
        var wrongExtension = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv("Code\tText\nA\tNorth"), "options.tsv");
        Assert.Equal("Forms.CatalogImport.FileUnreadable", invalid.Error.Code);
        Assert.Equal("Forms.CatalogImport.FileUnreadable", wrongExtension.Error.Code);
    }

    [Fact]
    public async Task Csv_preserves_literal_formula_text_empty_fields_and_record_numbers()
    {
        var result = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv("Code,Text,Detail\nA,=1+1,\n\nB,North,"), "options.csv");
        Assert.True(result.IsSuccess);
        Assert.Equal([1, 2, 4], result.Value.Rows.Select(row => row.Number));
        Assert.Equal("=1+1", result.Value.Rows[1].Cells[1].Text);
        Assert.Equal("", result.Value.Rows[1].Cells[2].Text);
        Assert.Equal("", result.Value.Rows[2].Cells[2].Text);
    }

    [Fact]
    public async Task Xlsx_reads_only_the_first_sheet_with_unformatted_canonical_scalars()
    {
        var bytes = FormSpreadsheetFixture.Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "Code"; sheet.Cell(1, 2).Value = "Text";
            sheet.Cell(2, 1).Value = "=1+1"; sheet.Cell(2, 2).Value = 1500.25;
            sheet.Cell(2, 2).Style.NumberFormat.Format = "€ #,##0.00";
            sheet.Cell(2, 3).Value = false; sheet.Cell(2, 4).Value = new DateTime(2026, 10, 22);
            sheet.Cell(2, 5).Value = 0.000000000000000000000000000001;
        }, second: true);

        var result = await FormSpreadsheetFixture.ReadAsync(bytes);

        Assert.True(result.IsSuccess);
        Assert.Equal("Options", result.Value.Name);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), result.Value.Sha256);
        Assert.Equal(2, result.Value.Rows.Count);
        Assert.Equal(["=1+1", "1500.25", "false", "2026-10-22", "0.000000000000000000000000000001"], result.Value.Rows[1].Cells.Select(cell => cell.Text));
    }

    [Fact]
    public async Task Xlsx_numeric_and_date_cells_are_canonical_while_text_cells_remain_human_input()
    {
        var bytes = FormSpreadsheetFixture.Workbook(sheet =>
        {
            sheet.Cell(1, 1).Value = "Number"; sheet.Cell(1, 2).Value = "Date";
            sheet.Cell(1, 3).Value = "Human number"; sheet.Cell(1, 4).Value = "Human date";
            sheet.Cell(1, 5).Value = "Exact text";
            sheet.Cell(2, 1).Value = 1500.25; sheet.Cell(2, 2).Value = new DateTime(2026, 4, 3);
            sheet.Cell(2, 3).Value = "1.500"; sheet.Cell(2, 4).Value = "3/4/2026";
            sheet.Cell(2, 5).Value = "0.1234567890123456789012345678";
        });

        var result = await FormSpreadsheetFixture.ReadAsync(bytes);

        Assert.True(result.IsSuccess);
        var cells = result.Value.Rows[1].Cells;
        Assert.Equal(["1500.25", "2026-04-03", "1.500", "3/4/2026", "0.1234567890123456789012345678"], cells.Select(cell => cell.Text));
        Assert.Equal([true, true, false, false, false], cells.Select(cell => cell.IsCanonicalValue));
    }

    [Fact]
    public async Task Csv_cells_keep_ambiguous_human_values_unmarked()
    {
        var bytes = FormSpreadsheetFixture.Csv("Code,Text,Value\nA,North,1.500\nB,South,3/4/2026\n");

        var result = await FormSpreadsheetFixture.ReadAsync(bytes, "options.csv");

        Assert.True(result.IsSuccess);
        Assert.Equal("1.500", result.Value.Rows[1].Cells[2].Text);
        Assert.Equal("3/4/2026", result.Value.Rows[2].Cells[2].Text);
        Assert.All(result.Value.Rows.SelectMany(row => row.Cells), cell => Assert.False(cell.IsCanonicalValue));
    }

    [Fact]
    public async Task Xlsx_uses_stale_formula_caches_and_reports_missing_or_error_caches_without_evaluating()
    {
        var bytes = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"), SheetStart + """
            <row r="1"><c r="A1" t="inlineStr"><is><t>Code</t></is></c></row>
            <row r="2"><c r="A2" t="str"><f>UNSUPPORTED_PRIVATE_FUNCTION()</f><v>cached</v></c>
              <c r="B2"><f>1+1</f><v>0</v></c><c r="C2"><f>1+1</f></c>
              <c r="D2" t="e"><f>1/0</f><v>#DIV/0!</v></c></row></sheetData>
            """);

        var result = await FormSpreadsheetFixture.ReadAsync(bytes);

        Assert.True(result.IsSuccess);
        var row = result.Value.Rows[1];
        Assert.Equal("cached", row.Cells[0].Text); Assert.Equal("0", row.Cells[1].Text);
        Assert.Equal([false, true, false, false], row.Cells.Select(cell => cell.IsCanonicalValue));
        Assert.Equal("Forms.CatalogImport.FormulaValueMissing", row.Cells[2].Error!.Code);
        Assert.Equal("Forms.CatalogImport.CellUnreadable", row.Cells[3].Error!.Code);
        Assert.Null(row.Cells[2].Text); Assert.Null(row.Cells[3].Text);
        Assert.DoesNotContain("PRIVATE", row.Cells[2].ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task File_size_is_bounded_without_owning_or_fully_reading_the_input_stream(bool seekable)
    {
        using var content = new FormSpreadsheetInputStream(FormCatalogImportLimits.MaximumFileBytes * 3L, seekable);
        var result = await FormSpreadsheetFixture.ReadAsync(content, "options.csv");
        Assert.Equal("Forms.CatalogImport.FileTooLarge", result.Error.Code);
        Assert.InRange(content.BytesRead, 0, FormCatalogImportLimits.MaximumFileBytes + 1L);
        if (seekable) Assert.Equal(0, content.BytesRead);
        Assert.False(content.WasDisposed);
    }

    [Fact]
    public async Task A_nonseekable_csv_is_read_once_and_keeps_the_callers_stream_open()
    {
        var bytes = FormSpreadsheetFixture.Csv("Code,Text\nA,North");
        using var content = new FormSpreadsheetInputStream(bytes.Length, content: bytes);
        var result = await FormSpreadsheetFixture.ReadAsync(content, "options.csv");
        Assert.True(result.IsSuccess);
        Assert.True(content.CanRead);
        Assert.Equal(bytes.Length, content.BytesRead);
    }

    [Fact]
    public async Task Cancellation_propagates_without_closing_the_callers_stream()
    {
        using var content = new MemoryStream(FormSpreadsheetFixture.Csv("Code,Text"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FormSpreadsheetFixture.ReadAsync(content, "options.csv", cancellation.Token));
        Assert.True(content.CanRead);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Archive_budget_uses_declared_and_effectively_delivered_bytes_before_loading_a_workbook(bool forgedLength)
    {
        var bytes = FormSpreadsheetFixture.ArchiveExpansion(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"), FormCatalogImportLimits.MaximumArchiveBytes + 1, forgedLength);
        var measured = FormSpreadsheetFixture.EffectiveEntryBytes(bytes, "xl/media/large.bin");
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        output.WriteLine("Synthetic unconsumed part; forged: " + forgedLength + "; declared bytes: " + measured.Declared + "; delivered bytes: " + measured.Read + "; reader success: " + result.IsSuccess);
        if (forgedLength)
        {
            Assert.Equal(1, measured.Declared);
            Assert.Equal(1, measured.Read);
            Assert.True(result.IsSuccess);
        }
        else
        {
            Assert.True(measured.Declared > FormCatalogImportLimits.MaximumArchiveBytes);
            Assert.True(measured.Read > FormCatalogImportLimits.MaximumArchiveBytes);
            Assert.Equal("Forms.CatalogImport.FileTooLarge", result.Error.Code);
        }
    }

    [Theory]
    [InlineData("xl/worksheets/sheet1.xml")]
    [InlineData("xl/sharedStrings.xml")]
    public async Task A_forged_consumed_part_is_bounded_by_ziparchive_and_rejected_as_unreadable(string part)
    {
        var bytes = FormSpreadsheetFixture.ConsumedArchiveExpansion(part);
        var measured = FormSpreadsheetFixture.EffectiveEntryBytes(bytes, part);
        output.WriteLine("Synthetic consumed part: " + part + "; declared bytes: " + measured.Declared + "; delivered bytes: " + measured.Read);
        Assert.Equal(1, measured.Declared);
        Assert.Equal(1, measured.Read);
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.FileUnreadable", result.Error.Code);
    }

    [Fact]
    public async Task Csv_allows_header_rules_and_ten_thousand_records_but_rejects_the_next_record()
    {
        var text = "Code,Text\nRules,Rules\n" + string.Concat(Enumerable.Range(0, BusinessFormCatalog.MaxItems).Select(index => "A" + index + ",North\n"));
        var allowed = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv(text), "options.csv");
        var excess = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv(text + "EXTRA,North\n"), "options.csv");
        Assert.True(allowed.IsSuccess); Assert.Equal(BusinessFormCatalog.MaxItems + 2, allowed.Value.Rows.Count);
        Assert.Equal("Forms.CatalogImport.TooManyRows", excess.Error.Code);
    }

    [Fact]
    public async Task Csv_rejects_a_sixty_fifth_column()
    {
        var result = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv(string.Join(',', Enumerable.Repeat("Column", 65))), "options.csv");
        Assert.Equal("Forms.CatalogImport.TooManyColumns", result.Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_sheet_is_bounded_by_content_count_before_closedxml_reads_any_invalid_scalar(bool columns)
    {
        var bytes = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code", second: true);
        bytes = FormSpreadsheetFixture.Sheet(bytes, SheetStart + "<row r=\"1\"><c r=\"A1\"><v>not-a-number</v></c></row></sheetData>");
        var oversized = columns ? "<row r=\"1\"><c r=\"BM1\"><v>1</v></c></row>"
            : string.Concat(Enumerable.Range(1, BusinessFormCatalog.MaxItems + 3).Select(number =>
                "<row r=\"" + number + "\"><c r=\"A" + number + "\"><v>1</v></c></row>"));
        bytes = FormSpreadsheetFixture.Sheet(bytes, SheetStart + oversized + "</sheetData>", 2);
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.Equal("Forms.CatalogImport." + (columns ? "TooManyColumns" : "TooManyRows"), result.Error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Sparse_worksheet_rows_keep_their_original_numbers_without_spending_the_data_count_budget(int sheetNumber)
    {
        var bytes = FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code", second: true);
        bytes = FormSpreadsheetFixture.Sheet(bytes, SheetStart + """
            <row r="1"><c r="A1" t="inlineStr"><is><t>Code</t></is></c><c r="B1" t="inlineStr"><is><t>Text</t></is></c></row>
            <row r="20000"><c r="A20000" t="inlineStr"><is><t>A</t></is></c><c r="B20000" t="inlineStr"><is><t>North</t></is></c></row></sheetData>
            """, sheetNumber);
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.True(result.IsSuccess);
        Assert.Equal(sheetNumber == 1 ? new[] { 1, 20000 } : [1], result.Value.Rows.Select(row => row.Number));
        if (sheetNumber == 1) Assert.Equal("North", result.Value.Rows[1].Cells[1].Text);
    }

    [Fact]
    public async Task Empty_csv_records_do_not_spend_the_data_count_budget_or_renumber_later_content()
    {
        const int emptyCount = 10050;
        var empty = string.Concat(Enumerable.Range(0, emptyCount).Select(index => (index % 3) switch
        {
            0 => "\n", 1 => " , \n", _ => "\"\",\"\"\n",
        }));
        var result = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv("Code,Text\n" + empty + "A,North\n"), "options.csv");
        Assert.True(result.IsSuccess);
        Assert.Equal([1, emptyCount + 2], result.Value.Rows.Select(row => row.Number));
        Assert.Equal("North", result.Value.Rows[1].Cells[1].Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_first_record_stays_the_header_instead_of_promoting_later_data(bool xlsx)
    {
        var bytes = xlsx
            ? FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"), SheetStart + """
                <row r="20000"><c r="A20000" t="inlineStr"><is><t>Code</t></is></c><c r="B20000" t="inlineStr"><is><t>Text</t></is></c></row></sheetData>
                """)
            : FormSpreadsheetFixture.Csv("\nCode,Text\nA,North\n");
        var result = await FormSpreadsheetFixture.ReadAsync(bytes, xlsx ? "options.xlsx" : "options.csv");
        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Rows[0].Number);
        Assert.All(result.Value.Rows[0].Cells, cell => Assert.True(string.IsNullOrEmpty(cell.Text)));
        Assert.Equal(xlsx ? 20000 : 2, result.Value.Rows[1].Number);
    }

    [Theory]
    [InlineData("<row r=\"1048577\"><c><v>1</v></c></row>")]
    [InlineData("<row r=\"1\"><c r=\"XFE1\"><v>1</v></c></row>")]
    public async Task Addresses_outside_the_excel_grid_remain_unreadable_files(string row)
    {
        var bytes = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"),
            SheetStart + row + "</sheetData>");
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.Equal("Forms.CatalogImport.FileUnreadable", result.Error.Code);
    }

    [Fact]
    public async Task An_unrepresentable_xml_row_number_is_an_unreadable_file_instead_of_an_unhandled_exception()
    {
        var bytes = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"),
            SheetStart + "<row r=\"99999999999999999999\"><c><v>1</v></c></row></sheetData>");
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.CatalogImport.FileUnreadable", result.Error.Code);
    }

    [Fact]
    public async Task Empty_formatting_does_not_expand_the_returned_grid()
    {
        var bytes = FormSpreadsheetFixture.Sheet(FormSpreadsheetFixture.Workbook(sheet => sheet.Cell(1, 1).Value = "Code"),
            SheetStart + "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Code</t></is></c></row><row r=\"1048576\"><c r=\"XFD1048576\" s=\"0\"/></row></sheetData>");
        var result = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Rows); Assert.Single(result.Value.Rows[0].Cells);
    }
}
