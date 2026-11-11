using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Export;
using ClosedXML.Excel;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// UB4-A, on the generated files: the Excel spreads a point over three number cells and the PDF writes the point with «Ver en el
// mapa» as a link of the PDF.
public sealed class FormLocationRenderingTests
{
    [Fact]
    public void The_excel_keeps_latitude_longitude_and_accuracy_as_numbers()
    {
        var field = Guid.NewGuid();
        var answered = new FormResponseCell(field, "location", "Answered", new StructuredRecordLocationValue(-34.603722m, -58.381592m, 2400));
        var withoutAccuracy = answered with { Value = new StructuredRecordLocationValue(-34.603722m, -58.381592m) };
        var older = new FormResponseCell(field, "location", "NotAsked", null);
        FormWorkbookCell[] Row(FormResponseCell cell) => [.. Enumerable.Range(0, 3).Select(part => FormExportPolicy.LocationCell(cell, part))];

        var bytes = new ClosedXmlFormWorkbookWriter().Write(new([new(["Lugar · Latitud", "Lugar · Longitud", "Lugar · Precisión (m)"],
            [Row(answered), Row(withoutAccuracy), Row(older)], "Responses")]), TestContext.Current.CancellationToken);

        using var workbook = new XLWorkbook(new MemoryStream(bytes)); var sheet = workbook.Worksheet(1);
        Assert.All(Enumerable.Range(1, 3), column => Assert.Equal(XLDataType.Number, sheet.Cell(2, column).DataType));
        Assert.Equal(-34.603722, sheet.Cell(2, 1).GetDouble(), 6);
        Assert.Equal(-58.381592, sheet.Cell(2, 2).GetDouble(), 6);
        Assert.Equal(2400, sheet.Cell(2, 3).GetDouble());
        Assert.True(sheet.Cell(3, 3).IsEmpty());
        Assert.All(Enumerable.Range(1, 3), column => Assert.Equal("—", sheet.Cell(4, column).GetString()));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task The_pdf_writes_the_point_with_a_link_to_the_map(string culture)
    {
        var fixture = new FormPdfRendererFixture(); var original = fixture.Snapshot.Response.Record;
        var fieldId = Guid.NewGuid();
        var field = new CanonicalFormField(fieldId, "place", "location",
            new Dictionary<string, string> { ["es"] = "Dónde estás", ["en"] = "Where you are" }, null, true, null, null);
        var record = original with
        {
            SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [field])),
            ValuesJson = JsonSerializer.Serialize(new[] { new { fieldId, value = new { latitude = -34.603722m, longitude = -58.381592m, accuracyMeters = 15 } } }),
            Files = [], References = [],
        };
        var request = fixture.Snapshot with
        {
            Response = fixture.Snapshot.Response with { Record = record },
            Snapshot = fixture.Snapshot.Snapshot with { Design = fixture.Snapshot.Snapshot.Design with { QuestionIds = [fieldId], IncludePhotos = false } },
        };

        var bytes = await fixture.Renderer.RenderAsync(request, culture, false, TestContext.Current.CancellationToken);

        var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
        Assert.Contains("/URI (https://www.google.com/maps?q=-34.603722,-58.381592)", text, StringComparison.Ordinal);
        await fixture.ExportAsync(bytes, "location-" + culture);
    }
}
