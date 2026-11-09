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

    [Fact]
    public void A_small_sheet_keeps_its_first_six_rows_with_content_whole()
    {
        FormSpreadsheetRow[] rows = [Row(1, "CUIT", "Razón social", "Zona"), .. Enumerable.Range(2, 9).Select(number => Row(number, $"30-{number:D8}-1", $"Empresa {number}", "Norte"))];

        var prepared = Prepare(Sheet(rows));

        Assert.Equal(("contratistas.xlsx", StructuredSpreadsheetKind.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 2048L, Sha, "Hoja1", 10, 3),
            (prepared.FileName, prepared.Kind, prepared.MediaType, prepared.Size, prepared.Sha256, prepared.SheetName, prepared.RowCount, prepared.ColumnCount));
        Assert.Equal([1, 2, 3, 4, 5, 6], prepared.Rows.Select(row => row.Number));
        Assert.Equal([new("A", "CUIT"), new("B", "Razón social"), new KeyValuePair<string, string>("C", "Zona")], prepared.Rows[0].Cells);
        Assert.Equal("Empresa 6", prepared.Rows[5].Cells["B"]);
    }

    [Fact]
    public void The_sample_is_a_compact_map_from_letter_to_text()
    {
        var json = JsonSerializer.Serialize(Prepare(Sheet(Row(1, "CUIT", null, "Zona"))), JsonSerializerOptions.Web);

        Assert.Contains("\"rows\":[{\"number\":1,\"cells\":{\"A\":\"CUIT\",\"C\":\"Zona\"}}]", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_csv_is_text_csv()
    {
        var preparation = FormAttachmentPreparationPolicy.Prepare("insumos.csv", StructuredSpreadsheetKind.Csv, 30, new("", Sha, [Row(1, "Código"), Row(2, "A1")]));

        Assert.Equal((StructuredSpreadsheetKind.Csv, "text/csv", ""), (preparation.Spreadsheet!.Kind, preparation.Spreadsheet.MediaType, preparation.Spreadsheet.SheetName));
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
        Assert.Equal(new KeyValuePair<string, string>("E", "Sur"), Assert.Single(prepared.Rows[3].Cells));
        Assert.Equal(new KeyValuePair<string, string>("D", "Oeste"), Assert.Single(prepared.Rows[4].Cells));
    }

    [Fact]
    public void Column_letters_go_from_A_to_BL()
    {
        var prepared = Prepare(Sheet(Row(1, [.. Enumerable.Range(1, FormCatalogImportLimits.MaximumColumns).Select(index => "H" + index)])));

        var letters = prepared.Rows[0].Cells.Keys.ToArray();
        Assert.Equal(64, prepared.ColumnCount);
        Assert.Equal(["A", "Z", "AA", "AZ", "BA", "BL"], new[] { 0, 25, 26, 51, 52, 63 }.Select(index => letters[index]));
        Assert.Equal("H27", prepared.Rows[0].Cells["AA"]);
    }

    [Fact]
    public void A_sheet_without_rows_with_content_is_empty()
    {
        Assert.Equal(StructuredAttachmentRejection.Empty,
            FormAttachmentPreparationPolicy.Prepare("vacia.csv", StructuredSpreadsheetKind.Csv, 12, Sheet(Row(1, null, ""), Row(3, " ", "\t"))).Rejection);
        Assert.Equal(StructuredAttachmentRejection.Empty, FormAttachmentPreparationPolicy.Prepare("vacia.csv", StructuredSpreadsheetKind.Csv, 0, Sheet()).Rejection);
    }

    [Fact]
    public void Every_cell_goes_up_to_80_characters_even_when_the_sheet_fits()
    {
        var prepared = Prepare(Sheet(Row(1, "Código", "Descripción"), Row(2, "A1", new string('x', 200)), Row(3, "A2", new string('y', 80))));

        Assert.Equal(new string('x', 79) + "…", prepared.Rows[1].Cells["B"]);
        Assert.Equal(new string('y', 80), prepared.Rows[2].Cells["B"]);
        Assert.Equal(3, prepared.Rows.Count);
    }

    [Fact]
    public void Wide_rows_lose_sample_rows_from_the_last_down_to_four_before_any_cell_is_cut()
    {
        // 32 columns of 80 characters: four rows fit in 12 KiB and five do not.
        var cell = new string('x', 80);
        FormSpreadsheetRow[] rows = [.. Enumerable.Range(1, 6).Select(number => Row(number, [.. Enumerable.Repeat(cell, 32)]))];

        var prepared = Prepare(Sheet(rows));

        Assert.Equal([1, 2, 3, 4], prepared.Rows.Select(row => row.Number));
        Assert.All(prepared.Rows, row => Assert.All(row.Cells.Values, text => Assert.Equal(cell, text)));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Theory]
    [InlineData('x')]
    [InlineData('ñ')]
    public void Sixty_four_columns_of_long_text_keep_four_rows_with_every_column_cut_by_half(char letter)
    {
        // Each «ñ» takes six bytes escaped: the cells need more halves than plain letters, but four rows still stay.
        var cell = new string(letter, 300);
        FormSpreadsheetRow[] rows = [.. Enumerable.Range(1, 6).Select(number => Row(number, [.. Enumerable.Repeat(cell, FormCatalogImportLimits.MaximumColumns)]))];

        var prepared = Prepare(Sheet(rows));

        Assert.Equal([1, 2, 3, 4], prepared.Rows.Select(row => row.Number));
        Assert.All(prepared.Rows, row => Assert.Equal(64, row.Cells.Count));
        var length = Assert.Single(prepared.Rows.SelectMany(row => row.Cells.Values).Select(text => text.Length).Distinct());
        Assert.InRange(length, 2, 40);
        Assert.All(prepared.Rows.SelectMany(row => row.Cells.Values), text => Assert.EndsWith("…", text, StringComparison.Ordinal));
        Assert.Equal((6, 64), (prepared.RowCount, prepared.ColumnCount));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Fact]
    public void Cells_of_32767_characters_are_cut_before_measuring()
    {
        // Excel's longest cell in every column: measuring the sample uncut would serialize megabytes several times.
        var cell = new string('x', 32767);
        FormSpreadsheetRow[] rows = [.. Enumerable.Range(1, 6).Select(number => Row(number, [.. Enumerable.Repeat(cell, FormCatalogImportLimits.MaximumColumns)]))];
        var sheet = Sheet(rows);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var prepared = Prepare(sheet);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(4, prepared.Rows.Count);
        Assert.InRange(allocated, 0, 4 * 1024 * 1024);
    }

    [Fact]
    public void The_sheet_name_is_bounded_and_never_cut_with_the_sample()
    {
        var name = new string('h', 300);
        var cell = new string('x', 300);

        var prepared = Prepare(new FormSpreadsheet(name, Sha, [Row(1, [.. Enumerable.Repeat(cell, FormCatalogImportLimits.MaximumColumns)])]), "planilla.xlsx");

        Assert.Equal(new string('h', 119) + "…", prepared.SheetName);
        Assert.Equal("planilla.xlsx", prepared.FileName);
    }

    [Theory]
    [InlineData("C:\\fakepath\\proveedores.csv", "proveedores.csv")]
    [InlineData("carpeta/proveedores", "proveedores")]
    [InlineData("", "")]
    public void The_file_name_keeps_only_its_last_segment(string name, string shown) =>
        Assert.Equal(shown, FormAttachmentPreparationPolicy.FileName(name));

    [Fact]
    public void The_file_name_is_bounded_once_at_120_characters()
    {
        var bounded = FormAttachmentPreparationPolicy.FileName(new string('n', 300) + ".csv");

        Assert.Equal(new string('n', 119) + "…", bounded);
        var cell = new string('x', 300);
        var prepared = Prepare(Sheet([.. Enumerable.Range(1, 6).Select(number => Row(number, [.. Enumerable.Repeat(cell, FormCatalogImportLimits.MaximumColumns)]))]), bounded);
        Assert.Equal(bounded, prepared.FileName);
    }

    [Fact]
    public void A_cut_never_splits_a_character()
    {
        var prepared = Prepare(Sheet(Row(1, new string('a', 78) + "😀" + new string('b', 300), "Zona")));

        Assert.Equal(new string('a', 78) + "…", prepared.Rows[0].Cells["A"]);
        Assert.Equal("Zona", prepared.Rows[0].Cells["B"]);
    }

    private static StructuredSpreadsheet Prepare(FormSpreadsheet sheet, string name = "contratistas.xlsx")
    {
        var preparation = FormAttachmentPreparationPolicy.Prepare(name, StructuredSpreadsheetKind.Xlsx, 2048, sheet);
        Assert.Null(preparation.Rejection);
        return preparation.Spreadsheet!;
    }

    private static int Bytes(StructuredSpreadsheet spreadsheet) => JsonSerializer.SerializeToUtf8Bytes(spreadsheet, JsonSerializerOptions.Web).Length;
    private static FormSpreadsheet Sheet(params FormSpreadsheetRow[] rows) => new("Hoja1", Sha, rows);
    private static FormSpreadsheetRow Row(int number, params string?[] cells) => new(number, [.. cells.Select(text => new FormSpreadsheetCell(text))]);
}
