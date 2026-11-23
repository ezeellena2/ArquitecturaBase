using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogFieldsPdfTests
{
    // R4 («nada debajo de otro dato»): each sealed datum of the option is a line of its own, «Pregunta · Dato», and the value of the
    // option stays alone on its line.
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Each_captured_data_field_is_a_line_of_its_own_named_after_the_question_with_its_localized_value(string culture)
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
        string Name(string es, string en) => culture == "es" ? es : en;
        var customer = Name("Cliente", "Customer");

        (string, string)[] expected =
        [
            (customer, "Pérez Hnos."),
            (customer + " · " + Name("Identificador", "Identifier"), "30-123"),
            (customer + " · " + Name("Importe", "Amount"), Name("1.500,25", "1,500.25")),
            (customer + " · " + Name("Activo", "Active"), "No"),
            (customer + " · " + Name("Fecha", "Date"), Name("23/10/2026", "10/23/2026")),
            (customer + " · " + Name("Zona", "Zone"), Name("Centro", "Center")),
            (customer + " · Nombre original", "0"),
        ];
        Assert.Equal(expected, Fields(question, JsonSerializer.SerializeToElement(reference.ItemId), culture, record));
        Assert.Equal("Pérez Hnos.", Value(question, reference.ItemId, culture, record));
        var request = fixture.Snapshot with { Response = fixture.Snapshot.Response with { Record = record } };
        var bytes = await fixture.Renderer.RenderAsync(request, culture, false, TestContext.Current.CancellationToken);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
        await fixture.ExportAsync(bytes, "catalog-fields-" + culture);
    }

    // R4 in a choice of several: a datum is one line with the values of the options that have it, by comma in the order of the options.
    [Fact]
    public void A_choice_of_several_writes_each_datum_once_with_the_values_of_its_options_in_their_order()
    {
        var fixture = new FormPdfRendererFixture(); var original = fixture.Snapshot.Response.Record;
        var taxId = Captured("Tax ID", "CUIT", "text", "data", JsonSerializer.SerializeToElement("FIRST"));
        var zone = Captured("Zone", "Zona", "text", "data", JsonSerializer.SerializeToElement("North"));
        var first = Assert.Single(original.References!) with { Text = "First", FieldValuesJson = JsonSerializer.Serialize(new[] { taxId, zone }, JsonSerializerOptions.Web) };
        var second = first with { ItemId = Guid.NewGuid(), Text = "Second",
            FieldValuesJson = JsonSerializer.Serialize(new[] { taxId with { Value = JsonSerializer.SerializeToElement("SECOND") } }, JsonSerializerOptions.Web) };
        var record = original with { References = [first, second] };
        var question = JsonSerializer.Deserialize<FormVersionSchema>(record.SchemaJson, JsonSerializerOptions.Web)!.Questions.Single(field => field.FieldId == first.FieldId) with { Type = "multiChoice", Selection = new(0, 3) };

        (string, string)[] expected = [("Customer", "Second, First"), ("Customer · Tax ID", "SECOND, FIRST"), ("Customer · Zone", "North")];
        Assert.Equal(expected, Fields(question, JsonSerializer.SerializeToElement(new[] { second.ItemId, first.ItemId }), "en", record));
    }

    private static string Value(CanonicalFormField question, Guid itemId, string culture, FormRecordStoredRow record)
    {
        var method = typeof(SkiaFormPdfRenderer).GetMethod("Value", BindingFlags.NonPublic | BindingFlags.Static)!;
        return Assert.IsType<string>(method.Invoke(null, [question, JsonSerializer.SerializeToElement(itemId), culture, record]));
    }
    // The lines the copy writes for an answered question: its label and value, then one per datum of the chosen options.
    private static (string, string)[] Fields(CanonicalFormField question, JsonElement answer, string culture, FormRecordStoredRow record)
    {
        var method = typeof(SkiaFormPdfRenderer).GetMethod("Fields", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return [.. Assert.IsAssignableFrom<IEnumerable<(string, string)>>(method.Invoke(null, [question, answer, culture, record]))];
    }
    private static FormCatalogCapturedField Captured(string en, string es, string type, string role, JsonElement value) =>
        new(Guid.NewGuid(), "field", Text(es, en), type, role, value);
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
