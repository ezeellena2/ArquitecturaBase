using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// Stage 2 of the assistant authoring: a real file of each case, built in memory, through the port and the import reader,
// without a database. The kind comes from the bytes, never from the extension.
public sealed class FormSpreadsheetAttachmentTests
{
    private const string Csv = "Código;Descripción\n1;Café\n2;Azúcar\n";
    private const string WorkbookType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml";

    [Theory]
    [InlineData("xls", FormSpreadsheetFormat.Legacy)]
    [InlineData("xls95", FormSpreadsheetFormat.Legacy)]
    [InlineData("encrypted", FormSpreadsheetFormat.Protected)]
    [InlineData("doc", FormSpreadsheetFormat.Unknown)]
    [InlineData("xlsx", FormSpreadsheetFormat.Xlsx)]
    [InlineData("xlsm", FormSpreadsheetFormat.MacroEnabled)]
    [InlineData("xlsb", FormSpreadsheetFormat.MacroEnabled)]
    [InlineData("macros-next-to-a-workbook", FormSpreadsheetFormat.MacroEnabled)]
    [InlineData("zip-without-manifest", FormSpreadsheetFormat.Unknown)]
    [InlineData("docx", FormSpreadsheetFormat.Unknown)]
    [InlineData("csv", FormSpreadsheetFormat.Csv)]
    [InlineData("windows-1252", FormSpreadsheetFormat.Unknown)]
    public async Task The_format_comes_from_the_bytes(string sample, FormSpreadsheetFormat format)
    {
        await using var services = new ServiceCollection().AddBusinessFormsInfrastructure().BuildServiceProvider();

        Assert.Equal(format, services.GetRequiredService<IFormSpreadsheetReader>().DetectFormat(Sample(sample)));
    }

    [Theory]
    [InlineData("precios.xls", "Workbook")]
    [InlineData("precios95.xls", "Book")]
    public async Task An_old_excel_is_legacy(string name, string stream)
    {
        var preparation = await PrepareAsync(FormSpreadsheetFixture.Compound(stream), name);

        Assert.Equal(StructuredAttachmentRejection.Legacy, preparation.Rejection);
    }

    [Fact]
    public async Task A_workbook_with_a_password_is_protected()
    {
        var preparation = await PrepareAsync(FormSpreadsheetFixture.Compound("EncryptionInfo", "EncryptedPackage"), "precios.xlsx");

        Assert.Equal(StructuredAttachmentRejection.Protected, preparation.Rejection);
    }

    // A Word older than 2007 («WordDocument») is a document the port names (FormDocumentAttachmentTests); an Outlook message is not.
    [Fact]
    public async Task Another_ole2_file_is_unreadable()
    {
        var preparation = await PrepareAsync(FormSpreadsheetFixture.Compound("__properties_version1.0"), "precios.xls");

        Assert.Equal((StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Unknown), (preparation.Rejection, preparation.FileKind));
    }

    [Theory]
    [InlineData("precios.xlsm")]
    [InlineData("precios.xlsx")]
    public async Task A_workbook_with_macros_is_rejected_by_its_manifest(string name)
    {
        var preparation = await PrepareAsync(FormSpreadsheetFixture.MacroEnabled(Workbook()), name);

        Assert.Equal(StructuredAttachmentRejection.MacroEnabled, preparation.Rejection);
    }

    [Fact]
    public async Task An_xlsx_named_csv_is_read_as_a_workbook_with_canonical_cells()
    {
        var bytes = Workbook();

        var prepared = await PreparedAsync(bytes, "contratistas.csv");

        Assert.Equal(("contratistas.csv", StructuredSpreadsheetKind.Xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Options", 3, 4),
            (prepared.FileName, prepared.Kind, prepared.MediaType, prepared.SheetName, prepared.RowCount, prepared.ColumnCount));
        Assert.Equal((bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes))), (prepared.Size, prepared.Sha256));
        Assert.Equal(["CUIT", "Razón social", "Alta", "Saldo"], prepared.Rows[0].Cells.Values);
        Assert.Equal(["30-71234567-8", "Agro Sur", "2026-11-09", "1500.25"], prepared.Rows[1].Cells.Values);
        Assert.Equal(["A", "B", "C", "D"], prepared.Rows[1].Cells.Keys);
    }

    [Fact]
    public async Task A_csv_named_xlsx_is_read_as_text_without_a_sheet_name()
    {
        var prepared = await PreparedAsync(FormSpreadsheetFixture.Csv(Csv), "insumos.xlsx");

        Assert.Equal(("insumos.xlsx", StructuredSpreadsheetKind.Csv, "text/csv", "", 3, 2),
            (prepared.FileName, prepared.Kind, prepared.MediaType, prepared.SheetName, prepared.RowCount, prepared.ColumnCount));
        Assert.Equal(["Código", "Descripción"], prepared.Rows[0].Cells.Values);
        Assert.Equal("Azúcar", prepared.Rows[2].Cells["B"]);
    }

    [Fact]
    public async Task The_import_still_names_a_csv_without_a_name_while_the_attachment_leaves_it_empty()
    {
        var imported = await FormSpreadsheetFixture.ReadAsync(FormSpreadsheetFixture.Csv(Csv), ".csv");

        Assert.Equal("CSV", imported.Value.Name);
        Assert.Equal("", (await PreparedAsync(FormSpreadsheetFixture.Csv(Csv), ".csv")).SheetName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_utf8_csv_reads_the_same_with_or_without_bom(bool bom)
    {
        var bytes = FormSpreadsheetFixture.Csv(Csv, bom);

        var prepared = await PreparedAsync(bytes, "insumos.csv");

        Assert.Equal("Código", prepared.Rows[0].Cells["A"]);
        Assert.Equal(["1", "Café"], prepared.Rows[1].Cells.Values);
        Assert.Equal((bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes))), (prepared.Size, prepared.Sha256));
    }

    [Fact]
    public async Task A_csv_saved_in_windows_1252_is_unreadable()
    {
        var preparation = await PrepareAsync(Windows1252(), "insumos.csv");

        Assert.Equal(StructuredAttachmentRejection.Unreadable, preparation.Rejection);
    }

    // A Word is a document now (FormDocumentAttachmentTests); a presentation is neither a document nor a spreadsheet.
    [Fact]
    public async Task A_zip_that_is_not_a_spreadsheet_is_unreadable()
    {
        var presentation = FormSpreadsheetFixture.Package(("/ppt/presentation.xml",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"));

        var preparation = await PrepareAsync(presentation, "proveedores.xlsx");

        Assert.Equal((StructuredAttachmentRejection.Unreadable, StructuredAttachmentFileKind.Unknown), (preparation.Rejection, preparation.FileKind));
    }

    [Fact]
    public async Task A_single_column_list_is_prepared()
    {
        var prepared = await PreparedAsync(FormSpreadsheetFixture.Csv("Provincia\nBuenos Aires\nCórdoba\nSanta Fe\n"), "provincias.csv");

        Assert.Equal((4, 1), (prepared.RowCount, prepared.ColumnCount));
        Assert.All(prepared.Rows, row => Assert.Equal("A", Assert.Single(row.Cells).Key));
        Assert.Equal("Córdoba", prepared.Rows[2].Cells["A"]);
    }

    [Fact]
    public async Task A_file_without_rows_with_content_is_empty()
    {
        Assert.Equal(StructuredAttachmentRejection.Empty, (await PrepareAsync([], "vacia.csv")).Rejection);
        Assert.Equal(StructuredAttachmentRejection.Empty, (await PrepareAsync(FormSpreadsheetFixture.Csv("\n,\n \n"), "vacia.csv")).Rejection);
        Assert.Equal(StructuredAttachmentRejection.Empty, (await PrepareAsync(FormSpreadsheetFixture.Workbook(_ => { }), "vacia.xlsx")).Rejection);
    }

    [Fact]
    public async Task More_than_the_import_limits_is_too_large()
    {
        var bytes = Enumerable.Repeat((byte)'a', FormCatalogImportLimits.MaximumFileBytes + 1).ToArray();
        var columns = FormSpreadsheetFixture.Csv(string.Join(';', Enumerable.Range(1, FormCatalogImportLimits.MaximumColumns + 1).Select(index => "C" + index)));
        var rows = FormSpreadsheetFixture.Csv(string.Concat(Enumerable.Range(0, BusinessFormCatalog.MaxItems + 3).Select(index => "A" + index + "\n")));

        Assert.Equal(StructuredAttachmentRejection.TooLarge, (await PrepareAsync(bytes, "grande.csv")).Rejection);
        Assert.Equal(StructuredAttachmentRejection.TooLarge, (await PrepareAsync(columns, "ancha.csv")).Rejection);
        Assert.Equal(StructuredAttachmentRejection.TooLarge, (await PrepareAsync(rows, "larga.csv")).Rejection);
    }

    private static byte[] Sample(string sample) => sample switch
    {
        "xls" => FormSpreadsheetFixture.Compound("Workbook"),
        "xls95" => FormSpreadsheetFixture.Compound("Book"),
        "encrypted" => FormSpreadsheetFixture.Compound("EncryptionInfo", "EncryptedPackage"),
        "doc" => FormSpreadsheetFixture.Compound("WordDocument"),
        "xlsx" => Workbook(),
        "xlsm" => FormSpreadsheetFixture.MacroEnabled(Workbook()),
        "xlsb" => FormSpreadsheetFixture.Package(("/xl/workbook.bin", "application/vnd.ms-excel.sheet.binary.macroEnabled.main")),
        "macros-next-to-a-workbook" => FormSpreadsheetFixture.Package(("/xl/workbook.xml", WorkbookType),
            ("/xl/macros.xml", "application/vnd.ms-excel.sheet.macroEnabled.main+xml")),
        "zip-without-manifest" => FormSpreadsheetFixture.Zip("xl/workbook.xml"),
        "docx" => FormSpreadsheetFixture.WordDocument(),
        "csv" => FormSpreadsheetFixture.Csv(Csv),
        "windows-1252" => Windows1252(),
        _ => throw new ArgumentOutOfRangeException(nameof(sample)),
    };

    // Excel in Spanish saves «CSV» in ANSI; for these letters Latin-1 writes the same bytes as Windows-1252.
    private static byte[] Windows1252() => Encoding.Latin1.GetBytes(Csv);

    private static byte[] Workbook() => FormSpreadsheetFixture.Workbook(sheet =>
    {
        sheet.Cell(1, 1).Value = "CUIT"; sheet.Cell(1, 2).Value = "Razón social"; sheet.Cell(1, 3).Value = "Alta"; sheet.Cell(1, 4).Value = "Saldo";
        sheet.Cell(2, 1).Value = "30-71234567-8"; sheet.Cell(2, 2).Value = "Agro Sur"; sheet.Cell(2, 3).Value = new DateTime(2026, 11, 9);
        sheet.Cell(2, 4).Value = 1500.25; sheet.Cell(2, 4).Style.NumberFormat.Format = "$ #,##0.00";
        sheet.Cell(3, 1).Value = "30-70000000-1"; sheet.Cell(3, 2).Value = "Campo Norte";
    });

    private static async Task<StructuredSpreadsheet> PreparedAsync(byte[] bytes, string name)
    {
        var preparation = await PrepareAsync(bytes, name);
        Assert.Null(preparation.Rejection);
        return preparation.Spreadsheet!;
    }

    private static Task<StructuredAttachmentPreparation> PrepareAsync(byte[] bytes, string name) => FormDocumentFixture.PrepareAsync(bytes, name);
}
