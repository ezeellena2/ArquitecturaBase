using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// VC1-A, task 27: to load a spreadsheet the rows of data of every sheet travel with their sheet and the headers of their sheet (its first row
// with content), up to 50 rows of data without counting the headers; building and importing keep the first sheet and its own limits.
public sealed class FormDocumentRowPreparationTests
{
    private const string Sha = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void The_rows_of_data_of_every_sheet_come_with_their_sheet_their_number_and_the_headers_of_their_sheet()
    {
        var rows = FormDocumentRowPolicy.DataRows([
            Sheet("Enero", Row(1, null, null), Row(2, "Fecha", "Monto"), Row(3, "2026-11-03", "95000"), Row(4, " ", ""), Row(6, null, "12500")),
            Sheet("Vacía"),
            Sheet("Febrero", Row(1, "Fecha", "Cliente"), Row(2, "2026-02-01", "Kiosco Este")),
            Sheet("Sólo encabezados", Row(1, "Fecha"))]);

        Assert.Equal([("Enero", 3), ("Enero", 6), ("Febrero", 2)], rows.Select(row => (row.Sheet, row.Number)));
        Assert.Equal(["Fecha", "Monto"], rows[0].Headers.Select(cell => cell.Text));
        Assert.Equal(["2026-11-03", "95000"], rows[0].Cells.Select(cell => cell.Text));
        Assert.Equal(["Fecha", "Cliente"], rows[2].Headers.Select(cell => cell.Text));
    }

    [Fact]
    public void The_sample_to_load_has_the_rows_of_every_sheet_with_their_sheet_and_counts_its_rows_of_data()
    {
        var prepared = PrepareRows(
            Sheet("Enero", Row(1, "Fecha", "Monto", "Cliente"), Row(2, "2026-11-03", "95000", "Ferretería Norte"), Row(3, "2026-11-04", "12500", null)),
            Sheet("Febrero", Row(1, "Fecha", "Monto"), Row(5, "2026-02-01", "8000")));

        Assert.Equal(("cobranzas.xlsx", "Enero", 3, 3, 3), (prepared.FileName, prepared.SheetName, prepared.RowCount, prepared.ColumnCount, prepared.DataRowCount));
        Assert.Equal([("Enero", 1), ("Enero", 2), ("Enero", 3), ("Febrero", 1), ("Febrero", 5)], prepared.Rows.Select(row => (row.Sheet!, row.Number)));
        Assert.Equal([new("A", "2026-02-01"), new KeyValuePair<string, string>("B", "8000")], prepared.Rows[4].Cells);
        var json = JsonSerializer.Serialize(prepared, JsonSerializerOptions.Web);
        Assert.Contains("{\"number\":5,\"cells\":{\"A\":\"2026-02-01\",\"B\":\"8000\"},\"sheet\":\"Febrero\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"dataRowCount\":3", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_csv_names_the_sheet_of_its_rows_empty_and_the_first_sheet_sample_stays_without_sheets()
    {
        var csv = FormAttachmentPreparationPolicy.PrepareRows("cobranzas.csv", StructuredSpreadsheetKind.Csv, 40, [Sheet("", Row(1, "Monto"), Row(2, "95000"))]);
        var first = FormAttachmentPreparationPolicy.Prepare("cobranzas.csv", StructuredSpreadsheetKind.Csv, 40, Sheet("", Row(1, "Monto"), Row(2, "95000")));

        Assert.Equal(("text/csv", ""), (csv.Spreadsheet!.MediaType, csv.Spreadsheet.Rows[1].Sheet));
        Assert.All(first.Spreadsheet!.Rows, row => Assert.Null(row.Sheet));
        Assert.Null(first.Spreadsheet.DataRowCount);
        Assert.DoesNotContain("sheet\"", JsonSerializer.Serialize(first.Spreadsheet.Rows, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(50, null)]
    [InlineData(51, StructuredAttachmentRejection.TooManyRows)]
    public void A_spreadsheet_to_load_has_at_most_fifty_rows_of_data_in_all_its_sheets_without_their_headers(int rows, StructuredAttachmentRejection? rejection)
    {
        var first = rows / 2;
        var preparation = FormAttachmentPreparationPolicy.PrepareRows("cobranzas.xlsx", StructuredSpreadsheetKind.Xlsx, 2048, [
            Sheet("Enero", [Row(1, "Monto"), .. Enumerable.Range(2, first).Select(number => Row(number, "100"))]),
            Sheet("Febrero", [Row(1, "Monto"), .. Enumerable.Range(2, rows - first).Select(number => Row(number, "200"))])]);

        Assert.Equal(rejection, preparation.Rejection);
        if (rejection is null) Assert.Equal(rows, preparation.Spreadsheet!.DataRowCount);
        else Assert.Equal(StructuredAttachmentFileKind.Spreadsheet, preparation.FileKind);
    }

    [Fact]
    public void A_spreadsheet_without_rows_of_data_has_nothing_to_load()
    {
        var preparation = FormAttachmentPreparationPolicy.PrepareRows("cobranzas.xlsx", StructuredSpreadsheetKind.Xlsx, 2048,
            [Sheet("Enero", Row(1, "Fecha", "Monto")), Sheet("Febrero")]);

        Assert.Equal((StructuredAttachmentRejection.Empty, StructuredAttachmentFileKind.Spreadsheet), (preparation.Rejection, preparation.FileKind));
    }

    [Fact]
    public void A_wide_sample_cuts_the_cells_of_the_rows_of_data_before_the_headers_and_keeps_every_row()
    {
        // 50 rows of 8 columns of 80 characters do not fit in 12 KiB whole: the data shrink first and every row stays for the model.
        var header = Enumerable.Range(1, 8).Select(index => "Columna con un nombre largo número " + index).ToArray();
        var data = new string('x', 80);
        var prepared = PrepareRows(Sheet("Enero", [Row(1, header), .. Enumerable.Range(2, 50).Select(number => Row(number, [.. Enumerable.Repeat(data, 8)]))]));

        Assert.Equal(51, prepared.Rows.Count);
        Assert.Equal(header, prepared.Rows[0].Cells.Values);
        Assert.All(prepared.Rows.Skip(1).SelectMany(row => row.Cells.Values), text => Assert.True(text.Length < 80 && text.EndsWith('…')));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Fact]
    public void A_sample_that_cannot_fit_whole_leaves_out_its_last_rows_once_its_data_are_cut_to_ten_characters_and_its_headers_to_twenty()
    {
        // 64 columns of long text in every row: cut as far as they go, the 50 rows do not fit, so the last rows are left out.
        var cell = new string('x', 300);
        var prepared = PrepareRows(Sheet("Enero", [.. Enumerable.Range(1, 51).Select(number => Row(number, [.. Enumerable.Repeat(cell, FormCatalogImportLimits.MaximumColumns)]))]));

        Assert.InRange(prepared.Rows.Count, 2, 50);
        Assert.Equal(Enumerable.Range(1, prepared.Rows.Count), prepared.Rows.Select(row => row.Number));
        Assert.Equal(50, prepared.DataRowCount);
        Assert.All(prepared.Rows[0].Cells.Values, text => Assert.Equal(new string('x', 19) + "…", text));
        Assert.All(prepared.Rows.Skip(1).SelectMany(row => row.Cells.Values), text => Assert.Equal(new string('x', 9) + "…", text));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
    }

    [Theory]
    [InlineData("100", 20)]
    [InlineData("2026-11-03", 15)]
    public void A_wide_sample_of_short_values_keeps_them_whole_and_leaves_out_its_last_rows(string value, int width)
    {
        // A cut ends in «…», which takes six bytes escaped: cutting a short value would only make it longer, so the rows go instead.
        var header = Enumerable.Range(1, width).Select(index => "Columna " + index).ToArray();
        var prepared = PrepareRows(Sheet("Enero", [Row(1, header), .. Enumerable.Range(2, 50).Select(number => Row(number, [.. Enumerable.Repeat(value, width)]))]));

        Assert.InRange(prepared.Rows.Count, 2, 50);
        Assert.Equal(header, prepared.Rows[0].Cells.Values);
        Assert.All(prepared.Rows.Skip(1).SelectMany(row => row.Cells.Values), text => Assert.Equal(value, text));
        Assert.InRange(Bytes(prepared), 1, StructuredSpreadsheet.MaximumBytes);
        Assert.True(Bytes(prepared with { Rows = [.. prepared.Rows, prepared.Rows[^1] with { Number = 99 }] }) > StructuredSpreadsheet.MaximumBytes);
    }

    [Fact]
    public void A_hidden_sheet_and_one_with_only_its_headers_are_neither_counted_nor_shown()
    {
        // A list for the dropdowns of a template, hidden, with more rows than the limit; and a sheet with nothing to load.
        var lists = Sheet("Listas", [Row(1, "Cliente"), .. Enumerable.Range(2, 80).Select(number => Row(number, "Cliente " + number))]) with { Hidden = true };
        FormSpreadsheet[] sheets = [Sheet("Enero", Row(1, "Fecha", "Monto"), Row(2, "2026-11-03", "95000")), lists, Sheet("Notas", Row(1, "Leer antes de cargar"))];

        var prepared = PrepareRows(sheets);

        Assert.Equal(1, prepared.DataRowCount);
        Assert.Equal(["Enero", "Enero"], prepared.Rows.Select(row => row.Sheet));
        Assert.Equal([("Enero", 2)], FormDocumentRowPolicy.DataRows(sheets).Select(row => (row.Sheet, row.Number)));
    }

    [Fact]
    public void Building_or_importing_with_a_sample_to_load_keeps_the_first_sheet_on_its_card()
    {
        var prepared = PrepareRows(Sheet("Clientes", Row(1, "CUIT", "Razón social"), Row(2, "30-71234567-8", "Agro Sur")),
            Sheet("Bajas", Row(1, "CUIT", "Motivo"), Row(2, "20-12345678-6", "Cerrada")));
        var columns = FormAuthoringSpreadsheetPolicy.Map(prepared, "", "A", "B", null, null, "text", []).Columns;

        var summary = FormAuthoringSpreadsheetPolicy.Summary(prepared, hasHeaders: true, columns);

        Assert.Equal(("Clientes", 1), (summary.SheetName, summary.RowCount));
        Assert.Equal(["CUIT", "Razón social"], summary.Columns.Select(column => column.Header));
        Assert.Equal(["30-71234567-8", "Agro Sur"], Assert.Single(summary.Sample));
    }

    private static StructuredSpreadsheet PrepareRows(params FormSpreadsheet[] sheets)
    {
        var preparation = FormAttachmentPreparationPolicy.PrepareRows("cobranzas.xlsx", StructuredSpreadsheetKind.Xlsx, 2048, sheets);
        Assert.Null(preparation.Rejection);
        return preparation.Spreadsheet!;
    }

    private static FormSpreadsheet Sheet(string name, params FormSpreadsheetRow[] rows) => new(name, Sha, rows);

    private static FormSpreadsheetRow Row(int number, params string?[] cells) => new(number, [.. cells.Select(text => new FormSpreadsheetCell(text))]);

    private static int Bytes(StructuredSpreadsheet sheet) => JsonSerializer.SerializeToUtf8Bytes(sheet, JsonSerializerOptions.Web).Length;
}
