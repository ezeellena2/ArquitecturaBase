using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogFieldsPdfTests
{
    [Theory]
    [InlineData("es", "Identificador", "1.500,25", "No", "23/10/2026", "Centro")]
    [InlineData("en", "Identifier", "1,500.25", "No", "10/23/2026", "Center")]
    public async Task Pdf_shows_only_captured_data_fields_with_localized_names_values_and_individual_name_fallback(
        string culture, string label, string amount, string boolean, string date, string source)
    {
        var fixture = new FormPdfRendererFixture();
        var original = fixture.Snapshot.Response.Record; var reference = Assert.Single(original.References!);
        var fields = new[]
        {
            Captured("Identifier", "Identificador", "text", "data", JsonSerializer.SerializeToElement("30-123")),
            Captured("Amount", "Importe", "decimal", "data", JsonSerializer.SerializeToElement("1500.25")),
            Captured("Active", "Activo", "boolean", "data", JsonSerializer.SerializeToElement(false)),
            Captured("Date", "Fecha", "date", "data", JsonSerializer.SerializeToElement("2026-10-23")),
            Captured("Zone", "Zona", "catalog", "data", JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D"))) with { Text = Text("Centro", "Center") },
            Captured("Hidden", "Oculto", "text", "filter", JsonSerializer.SerializeToElement("DO NOT PRINT")),
            Captured("", "Nombre original", "integer", "data", JsonSerializer.SerializeToElement(0)) with { Name = new Dictionary<string, string> { ["es"] = "Nombre original" } },
        };
        var record = original with { References = [reference with { FieldValuesJson = JsonSerializer.Serialize(fields, JsonSerializerOptions.Web) }] };
        var question = JsonSerializer.Deserialize<FormVersionSchema>(record.SchemaJson, JsonSerializerOptions.Web)!.Questions.Single(field => field.FieldId == reference.FieldId);
        var formatted = Value(question, reference.ItemId, culture, record);
        Assert.Contains(label + " 30-123", formatted, StringComparison.Ordinal);
        Assert.Contains(amount, formatted, StringComparison.Ordinal);
        Assert.Contains(boolean, formatted, StringComparison.Ordinal);
        Assert.Contains(date, formatted, StringComparison.Ordinal);
        Assert.Contains(source, formatted, StringComparison.Ordinal);
        Assert.Contains("Nombre original 0", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("DO NOT PRINT", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain(culture == "en" ? "Hidden" : "Oculto", formatted, StringComparison.Ordinal);
        var request = fixture.Snapshot with { Response = fixture.Snapshot.Response with { Record = record } };
        var bytes = await fixture.Renderer.RenderAsync(request, culture, false, TestContext.Current.CancellationToken);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        await fixture.ExportAsync(bytes, "catalog-fields-" + culture);
    }

    [Fact]
    public void Multiple_options_keep_their_fields_grouped_in_the_answer_order()
    {
        var fixture = new FormPdfRendererFixture(); var original = fixture.Snapshot.Response.Record;
        var first = Assert.Single(original.References!) with { Text = "First",
            FieldValuesJson = JsonSerializer.Serialize(new[] { Captured("Tax ID", "CUIT", "text", "data", JsonSerializer.SerializeToElement("FIRST")) }, JsonSerializerOptions.Web) };
        var second = first with { ItemId = Guid.NewGuid(), Text = "Second", FieldValuesJson = JsonSerializer.Serialize(new[] { Captured("Tax ID", "CUIT", "text", "data", JsonSerializer.SerializeToElement("SECOND")) }, JsonSerializerOptions.Web) };
        var record = original with { References = [first, second] };
        var question = JsonSerializer.Deserialize<FormVersionSchema>(record.SchemaJson, JsonSerializerOptions.Web)!.Questions.Single(field => field.FieldId == first.FieldId) with { Type = "multiChoice", Selection = new(0, 3) };
        var method = typeof(SkiaFormPdfRenderer).GetMethod("Value", BindingFlags.NonPublic | BindingFlags.Static)!;
        var text = Assert.IsType<string>(method.Invoke(null, [question, JsonSerializer.SerializeToElement(new[] { second.ItemId, first.ItemId }), "en", record]));
        Assert.Contains("Second\nTax ID SECOND", text, StringComparison.Ordinal);
        Assert.Contains("First\nTax ID FIRST", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf("Second", StringComparison.Ordinal) < text.IndexOf("First", StringComparison.Ordinal));
    }

    private static string Value(CanonicalFormField question, Guid itemId, string culture, FormRecordStoredRow record)
    {
        var method = typeof(SkiaFormPdfRenderer).GetMethod("Value", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsType<string>(method.Invoke(null, [question, JsonSerializer.SerializeToElement(itemId), culture, record]));
    }
    private static FormCatalogCapturedField Captured(string en, string es, string type, string role, JsonElement value) =>
        new(Guid.NewGuid(), "field", Text(es, en), type, role, value);
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
