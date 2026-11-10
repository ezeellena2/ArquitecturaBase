using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// CA1-A in the Flow (profile r13): the server computes a calculation when it receives the answers, so the Flow does not show it,
// leaves it out of the complete payload and of the prefill, and keeps it in the mapping. Without calculations r13 compiles the
// bytes of r12.
public sealed class WhatsAppFlowCalculationCompilerTests
{
    private static Dictionary<string, string> Text(string spanish, string english) => new() { ["es"] = spanish, ["en"] = english };

    private static StructuredFormQuestion Question(string type, string label = "Pregunta")
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text(label, label), null, false, type is "shortText" or "longText" ? 80 : null, null);
    }

    private static StructuredFormQuestion Calculation(string label, bool? repeated = null) =>
        Question("decimal", label) with { Calculated = true, Repeated = repeated };

    private static StructuredFormQuestion Block(int minimum, int maximum) =>
        Question("group") with { Label = Text("Artículos", "Items"), Required = minimum > 0, Rows = new(minimum, maximum) };

    private static StructuredFormCheckRequest Request(params StructuredFormQuestion[] questions) =>
        new(1, "es", Text("Factura", "Invoice"), Text("Enviar", "Send"), questions, ["es", "en"]);

    private static WhatsAppCompiledFlowResponse Flow(WhatsAppFlowCompilationResponse result)
    {
        Assert.Empty(result.Issues);
        return result.Flows.Single(flow => flow.Culture == "es");
    }

    private static JsonElement[] Screens(WhatsAppCompiledFlowResponse flow)
    {
        using var document = JsonDocument.Parse(flow.Json);
        return document.RootElement.GetProperty("screens").Clone().EnumerateArray().ToArray();
    }

    private static JsonElement[] Controls(JsonElement screen) =>
        screen.GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().ToArray();

    private static string[] Names(JsonElement screen) =>
        Controls(screen).Select(control => control.TryGetProperty("name", out var name) ? name.GetString()! : control.GetProperty("type").GetString()!).ToArray();

    private static JsonElement Payload(JsonElement screen) => Controls(screen)[^1].GetProperty("on-click-action").GetProperty("payload");

    private static WhatsAppFlowProjection Projection(WhatsAppCompiledFlowResponse flow) =>
        new(1, flow.Json, flow.Hash, WhatsAppFlowCompiler.ProfileRevision, WhatsAppFlowCompiler.CodecVersion, flow.Culture,
            flow.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type, field.Required, field.MaxLength,
                field.Choices, field.Files, field.Selection, field.Rows, field.Repeated, field.ChoosesFirst, field.Calculated, field.ShowWhen)).ToArray());

    [Fact]
    public void A_calculation_is_neither_shown_nor_sent_nor_prefilled_and_stays_in_the_mapping()
    {
        var quantity = Question("decimal", "Cantidad");
        var price = Question("decimal", "Precio");
        var total = Calculation("Total");
        var notes = Question("shortText", "Notas");
        var flow = Flow(WhatsAppFlowCompiler.Compile(Request(quantity, price, total, notes)));

        var screen = Assert.Single(Screens(flow));
        Assert.Equal([quantity.Key, price.Key, notes.Key, "Footer"], Names(screen));
        Assert.Equal([quantity.Key, price.Key, notes.Key], Payload(screen).EnumerateObject().Select(property => property.Name));
        var prefill = screen.GetProperty("data").GetProperty("prefill_FORM").GetProperty("properties");
        Assert.Equal([quantity.Key, price.Key, notes.Key], prefill.EnumerateObject().Select(property => property.Name));
        Assert.Equal(new bool?[] { null, null, true, null }, flow.Mapping.Select(field => field.Calculated));
        Assert.Equal(total.Key, flow.Mapping[2].Key);
        // The calculation is not a component: the same form without it counts the same.
        Assert.Equal(Flow(WhatsAppFlowCompiler.Compile(Request(quantity, price, notes))).ComponentCount, flow.ComponentCount);
        Assert.Contains("\"calculated\":true", JsonSerializer.Serialize(flow.Mapping[2], JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.DoesNotContain("calculated", JsonSerializer.Serialize(flow.Mapping[0], JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    [Fact]
    public void The_amount_of_each_item_stays_out_of_its_rows_and_a_total_alone_after_the_block_ends_on_the_closing_screen()
    {
        var customer = Question("shortText", "Cliente");
        var block = Block(1, 3);
        var quantity = Question("decimal", "Cantidad") with { Repeated = true };
        var price = Question("decimal", "Precio") with { Repeated = true };
        var amount = Calculation("Importe", repeated: true);
        var total = Calculation("Total");
        var flow = Flow(WhatsAppFlowCompiler.Compile(Request(customer, block, quantity, price, amount, total)));

        var screens = Screens(flow);
        // The customer, three items and, as nothing shows after the block, the closing screen of r12.
        Assert.Equal(["FORM", "FORM_A", "FORM_B", "FORM_C", "FORM_D"], screens.Select(screen => screen.GetProperty("id").GetString()));
        Assert.Equal([quantity.Key, price.Key, block.Key, "If"], Names(screens[1]));
        Assert.Equal([quantity.Key, price.Key, "Footer"], Names(screens[3]));
        Assert.Equal(["TextBody", "Footer"], Names(screens[4]));
        Assert.True(screens[4].GetProperty("terminal").GetBoolean());
        var payload = Payload(screens[4]).EnumerateObject().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(payload, key => key.StartsWith(amount.Key, StringComparison.Ordinal) || key == total.Key);
        Assert.Contains(price.Key + "_r3", payload);
        Assert.Equal(new bool?[] { null, null, null, null, true, true }, flow.Mapping.Select(field => field.Calculated));
    }

    [Fact]
    public void A_title_goes_before_the_first_question_it_shows_and_a_title_of_only_calculations_is_not_shown()
    {
        var price = Question("decimal", "Precio");
        var total = Calculation("Total");
        var notes = Question("shortText", "Notas");
        var vat = Calculation("IVA");
        var request = Request(price, total, notes, vat) with
        {
            Sections = [new(1, Text("Cierre", "Closing")), new(3, Text("Impuestos", "Taxes"))],
        };

        var controls = Controls(Assert.Single(Screens(Flow(WhatsAppFlowCompiler.Compile(request)))));

        Assert.Equal(["TextInput", "TextSubheading", "TextInput", "Footer"], controls.Select(control => control.GetProperty("type").GetString()));
        Assert.Equal("Cierre", controls[1].GetProperty("text").GetString());
    }

    [Theory]
    [InlineData("shortText", false)]
    [InlineData("decimal", true)]
    public void Only_an_optional_decimal_is_a_calculation(string type, bool required)
    {
        var calculation = Question(type, "Total") with { Calculated = true, Required = required };

        var result = WhatsAppFlowCompiler.Compile(Request(Question("decimal", "Precio"), calculation));

        Assert.Empty(result.Flows);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FlowErrors.UnsupportedConfiguration, issue.Error);
        Assert.Equal(calculation.FieldId, issue.FieldId);
    }

    [Fact]
    public void A_calculation_neither_takes_a_prefilled_value_nor_moves_the_diagnostics_of_the_questions_after_it()
    {
        var price = Question("decimal", "Precio");
        var total = Calculation("Total");
        var notes = Question("shortText", "Notas");
        var projection = Projection(Flow(WhatsAppFlowCompiler.Compile(Request(price, total, notes))));

        Assert.True(WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [price.Key] = "10" }).IsSuccess);
        Assert.Equal(FlowErrors.InvalidValue, WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [total.Key] = "10" }).Error);
        Assert.Equal("draft.fields[2].label.es", WhatsAppFlowDispatchService.Map(new(1, null, "label"), projection).Path);
    }
}
