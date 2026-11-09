using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// Stage 2 of the assistant authoring: the first sheet travels to the model with its counts and a sample, in 12 KiB at most.
public sealed class FormAttachmentPreparationPolicyTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void A_small_sheet_keeps_its_first_six_rows_with_content_whole()
    {
        FormSpreadsheetRow[] rows = [Row(1, "CUIT", "Razón social", "Zona"), .. Enumerable.Range(2, 9).Select(number => Row(number, $"30-{number:D8}-1", $"Empresa {number}", "Norte"))];

        var prepared = Prepare(Sheet(rows));

        Assert.Equal(("contratistas.xlsx", StructuredSpreadsheetKind.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 2048L, Sha, "Hoja1", 10, 3),
            (prepared.FileName, prepared.Kind, prepared.MediaType, prepared.Size, prepared.Sha256, prepared.SheetName, prepared.RowCount, prepared.ColumnCount));
        Assert.Equal([1, 2, 3, 4, 5, 6], prepared.Rows.Select(row => row.Number));
        Assert.Equal([new StructuredSpreadsheetCell("A", "CUIT"), new StructuredSpreadsheetCell("B", "Razón social"), new StructuredSpreadsheetCell("C", "Zona")], prepared.Rows[0].Cells);
        Assert.Equal("Empresa 6", prepared.Rows[5].Cells[1].Text);
    }

    [Fact]
    public void A_csv_is_text_csv()
    {
        var preparation = FormAttachmentPreparationPolicy.Prepare("insumos.csv", StructuredSpreadsheetKind.Csv, 30, Sheet(Row(1, "Código"), Row(2, "A1")));

        Assert.Equal((StructuredSpreadsheetKind.Csv, "text/csv"), (preparation.Spreadsheet!.Kind, preparation.Spreadsheet.MediaType));
    }

    [Fact]
    public void Rows_with_content_count_row_one_and_the_columns_reach_the_widest_one()
    {
        var sheet = Sheet(
            Row(1, "Código", "Nombre", null),
            Row(2, "A1", "Norte"),
            Row(3, " ", "", null),
            new FormSpreadsheetRow(4, [new FormSpreadsheetCell(null, FormCatalogImportErrors.FormulaValueMissing)]),
            Row(9, null, null, null, null, "Sur"),
            Row(20000, null, null, null, "Oeste"));

        var prepared = Prepare(sheet);

        // Whitespace is not content; a cell the reader could not read is, as in the import, but it has no text to show.
        Assert.Equal((5, 5), (prepared.RowCount, prepared.ColumnCount));
        Assert.Equal([1, 2, 4, 9, 20000], prepared.Rows.Select(row => row.Number));
        Assert.Empty(prepared.Rows[2].Cells);
        Assert.Equal(new StructuredSpreadsheetCell("E", "Sur"), Assert.Single(prepared.Rows[3].Cells));
        Assert.Equal(new StructuredSpreadsheetCell("D", "Oeste"), Assert.Single(prepared.Rows[4].Cells));
    }

    [Fact]
    public void Column_letters_go_from_A_to_BL()
    {
        var prepared = Prepare(Sheet(Row(1, [.. Enumerable.Range(1, FormCatalogImportLimits.MaximumColumns).Select(index => "H" + index)])));

        var letters = prepared.Rows[0].Cells.Select(cell => cell.Column).ToArray();
        Assert.Equal(64, prepared.ColumnCount);
        Assert.Equal(["A", "Z", "AA", "AZ", "BA", "BL"], new[] { 0, 25, 26, 51, 52, 63 }.Select(index => letters[index]));
        Assert.Equal("H27", prepared.Rows[0].Cells[26].Text);
    }

    [Fact]
    public void A_sheet_without_rows_with_content_is_empty()
    {
        Assert.Equal(StructuredAttachmentRejection.Empty,
            FormAttachmentPreparationPolicy.Prepare("vacia.csv", StructuredSpreadsheetKind.Csv, 12, Sheet(Row(1, null, ""), Row(3, " ", "\t"))).Rejection);
        Assert.Equal(StructuredAttachmentRejection.Empty, FormAttachmentPreparationPolicy.Prepare("vacia.csv", StructuredSpreadsheetKind.Csv, 0, Sheet()).Rejection);
    }

    [Fact]
    public void Sample_rows_go_from_the_last_before_any_cell_is_cut()
    {
        var description = new string('x', 2500);
        FormSpreadsheetRow[] rows = [Row(1, "Código", "Descripción"), .. Enumerable.Range(2, 5).Select(number => Row(number, "C" + number, description))];

        var prepared = Prepare(Sheet(rows));

        Assert.Equal([1, 2, 3, 4, 5], prepared.Rows.Select(row => row.Number));
        Assert.All(prepared.Rows.Skip(1), row => Assert.Equal(description, row.Cells[1].Text));
        Assert.Equal(6, prepared.RowCount);
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Fact]
    public void Cells_are_cut_to_80_characters_only_when_row_one_alone_does_not_fit()
    {
        string[] header = [.. Enumerable.Range(1, 63).Select(index => index + new string('h', 300)), "Zona"];
        FormSpreadsheetRow[] rows = [Row(1, header), .. Enumerable.Range(2, 5).Select(number => Row(number, "A" + number))];

        var prepared = Prepare(Sheet(rows));

        var row = Assert.Single(prepared.Rows);
        Assert.Equal((1, 64), (row.Number, row.Cells.Count));
        Assert.All(row.Cells.Take(63), cell => Assert.Equal(80, cell.Text.Length));
        Assert.Equal(header[0][..79] + "…", row.Cells[0].Text);
        Assert.Equal("Zona", row.Cells[63].Text);
        Assert.Equal((6, 64), (prepared.RowCount, prepared.ColumnCount));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Fact]
    public void A_row_one_that_does_not_fit_even_at_80_characters_keeps_every_column_shorter()
    {
        // Every «ñ» takes six bytes escaped: 64 columns of 80 of them do not fit in 12 KiB.
        var prepared = Prepare(Sheet(Row(1, [.. Enumerable.Repeat(new string('ñ', 100), FormCatalogImportLimits.MaximumColumns)]), Row(2, "x")));

        var row = Assert.Single(prepared.Rows);
        Assert.Equal(64, row.Cells.Count);
        var length = Assert.Single(row.Cells.Select(cell => cell.Text.Length).Distinct());
        Assert.InRange(length, 2, 79);
        Assert.All(row.Cells, cell => Assert.EndsWith("…", cell.Text, StringComparison.Ordinal));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Fact]
    public void A_long_file_name_counts_toward_the_budget_and_is_cut_like_a_cell()
    {
        var name = new string('n', 13000) + ".csv";

        var prepared = Prepare(Sheet(Row(1, "Código"), Row(2, "A1")), name);

        Assert.Equal(name[..79] + "…", prepared.FileName);
        Assert.Equal([1], prepared.Rows.Select(row => row.Number));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Fact]
    public void A_cut_never_splits_a_character()
    {
        string[] header = [.. Enumerable.Repeat(new string('a', 78) + "😀" + new string('b', 300), 63), "Zona"];

        var prepared = Prepare(Sheet(Row(1, header)));

        Assert.All(prepared.Rows[0].Cells.Take(63), cell => Assert.Equal(new string('a', 78) + "…", cell.Text));
    }

    private static StructuredSpreadsheet Prepare(FormSpreadsheet sheet, string name = "contratistas.xlsx")
    {
        var preparation = FormAttachmentPreparationPolicy.Prepare(name, StructuredSpreadsheetKind.Xlsx, 2048, sheet);
        Assert.Null(preparation.Rejection);
        return preparation.Spreadsheet!;
    }

    private static int Bytes(StructuredSpreadsheet spreadsheet) => JsonSerializer.SerializeToUtf8Bytes(spreadsheet, Web).Length;
    private static FormSpreadsheet Sheet(params FormSpreadsheetRow[] rows) => new("Hoja1", Sha, rows);
    private static FormSpreadsheetRow Row(int number, params string?[] cells) => new(number, [.. cells.Select(text => new FormSpreadsheetCell(text))]);
}
