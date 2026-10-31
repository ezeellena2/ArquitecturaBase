using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
using ClosedXML.Excel;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogExportWorkbookTests
{
    [Theory]
    [InlineData("es", BusinessFormCatalogValueType.Text)]
    [InlineData("en", BusinessFormCatalogValueType.Decimal)]
    public void A_template_contains_only_headers_and_the_recognizable_rules_row(string culture, BusinessFormCatalogValueType type)
    {
        var columns = FormCatalogImportTemplate.Columns(type, culture, "es");
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(columns.Select(column => column.Header).ToArray(),
            [columns.Select(column => new FormWorkbookCell("text", column.Rule)).ToArray()], "Template"), Ct);
        using var stream = new MemoryStream(bytes); using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal("", sheet.Cell(2, 1).GetString());
        Assert.Equal(columns.Select(column => column.Header), sheet.Row(1).Cells(1, columns.Count).Select(cell => cell.GetString()));
        Assert.Equal(columns.Select(column => column.Rule), sheet.Row(2).Cells(1, columns.Count).Select(cell => cell.GetString()));
        Assert.All(sheet.CellsUsed(), cell => Assert.False(cell.HasFormula));
    }

    [Theory]
    [InlineData("es", "123456789012345678")]
    [InlineData("en", "0.1234567890123456789012345678")]
    public void Catalog_round_trip_cells_are_exact_text_even_when_they_look_numeric_or_like_formulas(string culture, string canonical)
    {
        var columns = FormCatalogImportTemplate.Columns(BusinessFormCatalogValueType.Decimal, culture, "es");
        string[] values = ["=SUM(A1)", "=2+2", "English", "Detail", "English detail", "Yes", canonical];
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(columns.Select(column => column.Header).ToArray(),
            [values.Select(value => new FormWorkbookCell("text", value)).ToArray()], "Options"), Ct);
        using var stream = new MemoryStream(bytes); using var workbook = new XLWorkbook(stream);
        var row = workbook.Worksheet(1).Row(2).Cells(1, values.Length).ToArray();
        Assert.All(row, cell => { Assert.Equal(XLDataType.Text, cell.DataType); Assert.False(cell.HasFormula); });
        Assert.Equal(values, row.Select(cell => cell.GetString()));
        Assert.Equal(canonical, row[^1].GetString());
    }

    // The real XLSX: what a person reads in Excel, and the same bytes read back by the importer without changes.
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task An_exported_catalog_reads_as_a_person_sees_it_and_reimports_without_changes(string culture)
    {
        var catalogId = Guid.CreateVersion7();
        var sourceId = Guid.CreateVersion7();
        // The source repeats «Insumos» between two active options and «Granos» between a deactivated option and an active one.
        FormCatalogStoredItemRow supplies = Option(sourceId, "INS-1", "Insumos", "Supplies", 0, true), inputs = Option(sourceId, "INS-2", "Insumos", "Inputs", 1, true),
            seeds = Option(sourceId, "SEM", "Semillas", "Seeds", 2, false), oldGrains = Option(sourceId, "GRA-OLD", "Granos", "Grains", 3, false),
            grains = Option(sourceId, "GRA", "Granos", "Grains", 4, true);
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> { [sourceId] = [supplies, inputs, seeds, oldGrains, grains] };
        var fields = new[]
        {
            Field("tipo", "catalog", "Tipo de producto", "Product type", 0) with { SourceCatalogId = sourceId }, Field("stock", "integer", "Stock", "Stock", 1),
            Field("precio", "decimal", "Precio", "Price", 2), Field("alta", "date", "Alta", "Since", 3), Field("propio", "boolean", "Propio", "Own", 4),
            Field("nota", "text", "Nota", "Note", 5),
        };
        FormCatalogStoredItemRow[] items =
        [
            Item(catalogId, "A", 0, true, "1500.25", Values(fields, supplies.Id, 1500L, "1234.5", "2026-10-22", true, "=2+2")),
            Item(catalogId, "B", 1, false, "0.1234567890123456789012345678",
                Values(fields, inputs.Id, 123456789012345678L, "12345678.123456789", "1850-05-01", false, null)),
            Item(catalogId, "C", 2, true, "-7", Values(fields, seeds.Id, -42L, "0.001", "2000-02-29", null, "Texto")),
            Item(catalogId, "D", 3, true, "0", Values(fields, oldGrains.Id)),
            Item(catalogId, "E", 4, true, "1", Values(fields, grains.Id)),
        ];
        var columns = FormCatalogExportPolicy.Columns(BusinessFormCatalogValueType.Decimal, culture, "es", fields, translation: culture != "es");
        var rows = FormCatalogExportPolicy.Rows(columns, items, BusinessFormCatalogValueType.Decimal, "es", fields, sources, culture);

        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(columns.Select(column => column.Header).ToArray(), rows, "Opciones"), Ct);

        using (var stream = new MemoryStream(bytes))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet(1);
            var width = columns.Count;
            Assert.Equal(culture == "es" ? ["Valor (decimal)", "Tipo de producto", "Stock", "Precio", "Alta", "Propio", "Nota"]
                : ["Value (decimal)", "Product type", "Stock", "Price", "Since", "Own", "Note"], sheet.Row(1).Cells(width - 6, width).Select(cell => cell.GetString()));
            var locale = CultureInfo.GetCultureInfo(culture == "es" ? "es-AR" : "en-US");
            string[] Shown(int row) => sheet.Row(row).Cells(width - 6, width).Select(cell => cell.GetFormattedString(locale)).ToArray();
            Assert.Equal(culture == "es" ? ["1.500,25", "Insumos", "1.500", "1.234,5", "22/10/2026", "Sí", "=2+2"]
                : ["1,500.25", "Supplies", "1,500", "1,234.5", "10/22/2026", "Yes", "=2+2"], Shown(2));
            // Beyond 15 digits, or before 1900, Excel cannot hold the value: the cell is its exact text.
            Assert.Equal(["0.1234567890123456789012345678", culture == "es" ? "Insumos" : "Inputs", "123456789012345678", "12345678.123456789",
                "1850-05-01", "No", ""], Shown(3));
            Assert.Equal(culture == "es" ? ["-7", "Semillas", "-42", "0,001", "29/02/2000", "", "Texto"]
                : ["-7", "Seeds", "-42", "0.001", "02/29/2000", "", "Texto"], Shown(4));
            // Its text would name the active «Granos»: the deactivated option goes by its code.
            Assert.Equal("GRA-OLD", sheet.Cell(5, width - 5).GetString());
            Assert.Equal(culture == "es" ? "Granos" : "Grains", sheet.Cell(6, width - 5).GetString());
            Assert.Equal([XLDataType.Number, XLDataType.Text, XLDataType.Number, XLDataType.Number, XLDataType.DateTime, XLDataType.Text],
                sheet.Row(2).Cells(width - 6, width - 1).Select(cell => cell.DataType));
            Assert.All(sheet.CellsUsed(), cell => Assert.False(cell.HasFormula));
        }
        var read = await FormSpreadsheetFixture.ReadAsync(bytes);
        Assert.True(read.IsSuccess);
        var plan = FormCatalogImportPolicy.Build(new(catalogId, 1, BusinessFormCatalogValueType.Decimal, "es", items, Fields: fields, Sources: sources),
            read.Value, FormCatalogImportMode.Replace, culture);
        Assert.True(plan.IsSuccess);
        Assert.Empty(plan.Value.Preview.Issues);
        Assert.Equal(items.Length, plan.Value.Preview.Counts.Unchanged);
        Assert.Empty(plan.Value.Items);
    }

    private static BusinessFormCatalogField Field(string key, string type, string spanish, string english, int position) => new(Guid.CreateVersion7(),
        key, new Dictionary<string, string> { ["es"] = spanish, ["en"] = english }, type, null, false, false, null, null, type == "text" ? 300 : null,
        "data", position, true);

    private static FormCatalogStoredItemRow Option(Guid catalogId, string code, string spanish, string english, int position, bool active) =>
        new(Guid.CreateVersion7(), catalogId, code, spanish, english, null, null, position, active);

    private static FormCatalogStoredItemRow Item(Guid catalogId, string code, int position, bool active, string value, string fields) =>
        new(Guid.CreateVersion7(), catalogId, code, "Opción " + code, null, null, null, position, active, value, fields);

    // The stored values of the fields, in their order; null or missing leaves a field without value.
    private static string Values(IReadOnlyList<BusinessFormCatalogField> fields, params object?[] values) =>
        JsonSerializer.Serialize(fields.Zip(values).Where(pair => pair.Second is not null)
            .ToDictionary(pair => pair.First.Id.ToString("D"), pair => pair.Second), JsonSerializerOptions.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
