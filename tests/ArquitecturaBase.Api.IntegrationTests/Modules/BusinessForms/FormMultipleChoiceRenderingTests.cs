using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;
using ClosedXML.Excel;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormMultipleChoiceRenderingTests
{
    [Theory]
    [InlineData("Integer", "123456789012345678", "0")]
    [InlineData("Decimal", "0.1234567890123456789012345678", "-1500.5")]
    [InlineData("Date", "2026-10-22", "2026-10-23")]
    public void A_multi_value_XLSX_cell_preserves_canonical_text_for_zero_one_and_two_choices(string type, string first, string second)
    {
        var values = new StructuredRecordChoiceValue[]
        {
            new(Guid.NewGuid().ToString("D"), "Primera", Value: first, ValueType: type),
            new(Guid.NewGuid().ToString("D"), "Segunda", Value: second, ValueType: type),
        };
        var field = Guid.NewGuid();
        FormWorkbookCell Cell(string state, IReadOnlyList<StructuredRecordChoiceValue> selected) =>
            FormExportPolicy.CatalogValue(new(field, "multiChoice", state, selected));
        var bytes = new ClosedXmlFormWorkbookWriter().Write(new(["Riesgos · valor"],
            [new[] { Cell("Unanswered", []) }, new[] { Cell("Answered", [values[0]]) }, new[] { Cell("Answered", [values[1], values[0]]) }], "Responses"), TestContext.Current.CancellationToken);
        using var workbook = new XLWorkbook(new MemoryStream(bytes)); var sheet = workbook.Worksheet(1);
        Assert.True(sheet.Cell(2, 1).IsEmpty());
        Assert.Equal(XLDataType.Text, sheet.Cell(3, 1).DataType); Assert.Equal(first, sheet.Cell(3, 1).GetString());
        Assert.Equal(XLDataType.Text, sheet.Cell(4, 1).DataType); Assert.Equal(second + "; " + first, sheet.Cell(4, 1).GetString());
        Assert.False(sheet.Cell(3, 1).HasFormula); Assert.False(sheet.Cell(4, 1).HasFormula);
    }

    [Theory]
    [InlineData("Integer", "123456789012345678", "0", "es", "123.456.789.012.345.678", "0")]
    [InlineData("Integer", "123456789012345678", "0", "en", "123,456,789,012,345,678", "0")]
    [InlineData("Decimal", "0.1234567890123456789012345678", "0", "es", "0,1234567890123456789012345678", "0,00")]
    [InlineData("Decimal", "0.1234567890123456789012345678", "0", "en", "0.1234567890123456789012345678", "0.00")]
    [InlineData("Date", "2026-10-22", "2026-10-23", "es", "22/10/2026", "23/10/2026")]
    [InlineData("Date", "2026-10-22", "2026-10-23", "en", "10/22/2026", "10/23/2026")]
    public async Task A_multi_value_PDF_lists_each_label_with_its_localized_captured_value_in_order(
        string type, string first, string second, string culture, string firstFormatted, string secondFormatted)
    {
        var fixture = new FormPdfRendererFixture(); var original = fixture.Snapshot.Response.Record;
        var fieldId = Guid.NewGuid(); var catalog = Guid.NewGuid(); var firstId = Guid.NewGuid(); var secondId = Guid.NewGuid();
        var field = new CanonicalFormField(fieldId, "risks", "multiChoice",
            new Dictionary<string, string> { ["es"] = "Riesgos", ["en"] = "Risks" }, null, false, null, null, Source: new(catalog), Selection: new(0, 3));
        var record = original with
        {
            SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [field])),
            ValuesJson = JsonSerializer.Serialize(new[] { new { fieldId, value = new[] { secondId, firstId } } }), Files = [],
            References = [new(original.RecordId, fieldId, catalog, 2, firstId, "Primera", null, "Tipos", Value: first, ValueType: type),
                new(original.RecordId, fieldId, catalog, 2, secondId, "Segunda", null, "Tipos", Value: second, ValueType: type)],
        };
        var answer = JsonSerializer.SerializeToElement(new[] { secondId, firstId });
        var formatter = typeof(SkiaFormPdfRenderer).GetMethod("Value", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal("Segunda · " + secondFormatted + ", Primera · " + firstFormatted,
            Assert.IsType<string>(formatter.Invoke(null, [field, answer, culture, record])));
        var request = fixture.Snapshot with
        {
            Response = fixture.Snapshot.Response with { Record = record },
            Snapshot = fixture.Snapshot.Snapshot with { Design = fixture.Snapshot.Snapshot.Design with { QuestionIds = [fieldId], IncludePhotos = false } },
        };
        var bytes = await fixture.Renderer.RenderAsync(request, culture, false, TestContext.Current.CancellationToken);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        await fixture.ExportAsync(bytes, "multi-catalog-" + type.ToLowerInvariant() + "-" + culture);
    }
}
