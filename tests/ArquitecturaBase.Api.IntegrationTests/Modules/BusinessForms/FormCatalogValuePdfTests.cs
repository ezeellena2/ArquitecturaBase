using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogValuePdfTests
{
    [Theory]
    [InlineData("Integer", "123456789012345678", "es", "123.456.789.012.345.678")]
    [InlineData("Integer", "123456789012345678", "en", "123,456,789,012,345,678")]
    [InlineData("Decimal", "1500", "es", "1.500,00")]
    [InlineData("Decimal", "1500", "en", "1,500.00")]
    [InlineData("Decimal", "0.1234567890123456789012345678", "es", "0,1234567890123456789012345678")]
    [InlineData("Decimal", "0.1234567890123456789012345678", "en", "0.1234567890123456789012345678")]
    [InlineData("Date", "2026-10-08", "es", "8/10/2026")]
    [InlineData("Date", "2026-10-08", "en", "10/8/2026")]
    public async Task A_captured_catalog_value_uses_its_type_and_localized_format_without_rounding(string valueType, string value, string culture, string formatted)
    {
        var fixture = new FormPdfRendererFixture();
        var original = fixture.Snapshot.Response.Record;
        var reference = Assert.Single(original.References!);
        var record = original with { References = [reference with { Value = value, ValueType = valueType }] };
        var schema = JsonSerializer.Deserialize<FormVersionSchema>(record.SchemaJson, JsonSerializerOptions.Web)!;
        var field = schema.Questions.Single(question => question.FieldId == reference.FieldId);
        var answer = JsonSerializer.SerializeToElement(reference.ItemId);
        var formatter = typeof(SkiaFormPdfRenderer).GetMethod("Value", BindingFlags.NonPublic | BindingFlags.Static)!;

        var text = Assert.IsType<string>(formatter.Invoke(null, [field, answer, culture, record]));

        Assert.Equal(reference.Text + " · " + formatted, text);
        var request = fixture.Snapshot with { Response = fixture.Snapshot.Response with { Record = record } };
        var bytes = await fixture.Renderer.RenderAsync(request, culture, false, TestContext.Current.CancellationToken);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        await fixture.ExportAsync(bytes, "catalog-" + valueType.ToLowerInvariant() + "-" + culture + (value.StartsWith("0.", StringComparison.Ordinal) ? "-precision" : ""));
    }

    [Fact]
    public void Legacy_catalog_references_keep_their_captured_text_without_a_value_suffix()
    {
        var fixture = new FormPdfRendererFixture();
        var record = fixture.Snapshot.Response.Record;
        var reference = Assert.Single(record.References!);
        var field = JsonSerializer.Deserialize<FormVersionSchema>(record.SchemaJson, JsonSerializerOptions.Web)!.Questions
            .Single(question => question.FieldId == reference.FieldId);
        var formatter = typeof(SkiaFormPdfRenderer).GetMethod("Value", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(reference.Text, formatter.Invoke(null, [field, JsonSerializer.SerializeToElement(reference.ItemId), "es", record]));
    }
}
