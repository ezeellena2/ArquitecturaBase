using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// The block of rows in the Flow (RG3-A, RG7-A, RG8-A): a screen per row up to ten, «¿Agregás otro renglón?» from the minimum
// with a Footer in each branch of an If, and each row's answers under field_<question>_r<n> in the complete payload.
public sealed class WhatsAppFlowRowsCompilerTests
{
    private static Dictionary<string, string> Text(string spanish, string english) => new() { ["es"] = spanish, ["en"] = english };

    private static StructuredFormQuestion Question(string type, string label = "Pregunta", int? maxLength = null)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text(label, label), null, false,
            type is "shortText" or "longText" ? maxLength ?? 80 : null, null);
    }

    private static StructuredFormQuestion Block(int minimum, int maximum) =>
        Question("group") with { Label = Text("Artículos", "Items"), Required = minimum > 0, Rows = new(minimum, maximum) };

    private static StructuredFormQuestion Row(StructuredFormQuestion question) => question with { Repeated = true };

    private static StructuredFormCheckRequest Request(params StructuredFormQuestion[] questions) =>
        new(1, "es", Text("Recepción", "Receipt"), Text("Enviar", "Send"), questions, ["es", "en"]);

    private static WhatsAppCompiledFlowResponse Flow(WhatsAppFlowCompilationResponse result, string culture = "es")
    {
        Assert.Empty(result.Issues);
        return result.Flows.Single(flow => flow.Culture == culture);
    }

    private static JsonElement[] Screens(WhatsAppCompiledFlowResponse flow)
    {
        using var document = JsonDocument.Parse(flow.Json);
        return document.RootElement.GetProperty("screens").Clone().EnumerateArray().ToArray();
    }

    private static JsonElement[] Controls(JsonElement screen) =>
        screen.GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().ToArray();

    private static string? Next(JsonElement footer) => footer.GetProperty("on-click-action").GetProperty("next").GetProperty("name").GetString();

    private static WhatsAppFlowProjection Projection(WhatsAppCompiledFlowResponse flow) =>
        new(1, flow.Json, flow.Hash, WhatsAppFlowCompiler.ProfileRevision, WhatsAppFlowCompiler.CodecVersion, flow.Culture,
            flow.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type, field.Required, field.MaxLength,
                field.Choices, field.Files, field.Selection, field.Rows, field.Repeated)).ToArray());

    [Fact]
    public void A_block_compiles_a_screen_per_row_that_asks_from_the_minimum_with_a_footer_in_each_branch()
    {
        var supplier = Question("shortText", "Proveedor");
        var block = Block(1, 3);
        var quantity = Row(Question("decimal", "Cantidad"));
        var notes = Row(Question("longText", "Observaciones", 200));
        var remito = Question("shortText", "Remito");
        var result = WhatsAppFlowCompiler.Compile(Request(supplier, block, quantity, notes, remito));

        var screens = Screens(Flow(result));

        Assert.Equal(new[] { "FORM", "FORM_A", "FORM_B", "FORM_C", "FORM_D" }, screens.Select(screen => screen.GetProperty("id").GetString()));
        Assert.Equal(new[] { "Artículos · renglón 1", "Artículos · renglón 2", "Artículos · renglón 3" },
            screens[1..4].Select(screen => screen.GetProperty("title").GetString()));
        Assert.Equal("Items · row 2", Screens(Flow(result, "en"))[2].GetProperty("title").GetString());
        var first = Controls(screens[1]);
        Assert.Equal(new[] { quantity.Key, notes.Key, block.Key }, first[..3].Select(control => control.GetProperty("name").GetString()));
        Assert.Equal("RadioButtonsGroup", first[2].GetProperty("type").GetString());
        Assert.Equal("¿Agregás otro renglón?", first[2].GetProperty("label").GetString());
        Assert.True(first[2].GetProperty("required").GetBoolean());
        Assert.Equal("If", first[3].GetProperty("type").GetString());
        Assert.Equal("${form." + block.Key + "} == 'true'", first[3].GetProperty("condition").GetString());
        Assert.Equal("FORM_B", Next(first[3].GetProperty("then")[0]));
        Assert.Equal("FORM_D", Next(first[3].GetProperty("else")[0]));
        var last = Controls(screens[3]);
        Assert.Equal(3, last.Length);
        Assert.Equal("FORM_D", Next(last[^1]));
        Assert.All(screens[..4], screen => Assert.False(screen.TryGetProperty("terminal", out _)));
        var payload = Controls(screens[4])[^1].GetProperty("on-click-action").GetProperty("payload");
        Assert.Equal("${screen.FORM.form." + supplier.Key + "}", payload.GetProperty(supplier.Key).GetString());
        Assert.Equal("${screen.FORM_B.form." + quantity.Key + "}", payload.GetProperty(quantity.Key + "_r2").GetString());
        Assert.Equal("${screen.FORM_C.form." + notes.Key + "}", payload.GetProperty(notes.Key + "_r3").GetString());
        Assert.Equal("${screen.FORM_A.form." + block.Key + "}", payload.GetProperty(block.Key + "_r1").GetString());
        Assert.Equal("${form." + remito.Key + "}", payload.GetProperty(remito.Key).GetString());
        Assert.Equal(10, payload.EnumerateObject().Count());
        var mapping = result.Flows[0].Mapping;
        Assert.Equal(new StructuredFormRowSettings(1, 3), mapping[1].Rows);
        Assert.Equal(new bool?[] { null, null, true, true, null }, mapping.Select(field => field.Repeated));
    }

    [Fact]
    public void Without_questions_after_the_block_each_row_that_can_end_completes_with_what_was_visited()
    {
        var supplier = Question("shortText", "Proveedor");
        var block = Block(2, 4);
        var quantity = Row(Question("decimal", "Cantidad"));

        var screens = Screens(Flow(WhatsAppFlowCompiler.Compile(Request(supplier, block, quantity))));

        Assert.Equal(5, screens.Length);
        var underMinimum = Controls(screens[1]);
        Assert.Equal(2, underMinimum.Length);
        Assert.Equal("FORM_B", Next(underMinimum[^1]));
        Assert.False(screens[1].TryGetProperty("terminal", out _));
        var ending = Controls(screens[2])[^1].GetProperty("else")[0];
        Assert.Equal("Enviar", ending.GetProperty("label").GetString());
        var payload = ending.GetProperty("on-click-action").GetProperty("payload");
        Assert.Equal(new[] { supplier.Key, quantity.Key + "_r1", quantity.Key + "_r2", block.Key + "_r2" }.Order(StringComparer.Ordinal),
            payload.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        Assert.Equal("${form." + quantity.Key + "}", payload.GetProperty(quantity.Key + "_r2").GetString());
        Assert.True(screens[2].GetProperty("terminal").GetBoolean());
        Assert.True(screens[3].GetProperty("terminal").GetBoolean());
        Assert.True(screens[4].GetProperty("terminal").GetBoolean());
        Assert.Equal(1 + 4 + 2, Controls(screens[4])[^1].GetProperty("on-click-action").GetProperty("payload").EnumerateObject().Count());
    }

    [Fact]
    public void A_block_with_a_minimum_of_zero_asks_before_the_first_row()
    {
        var block = Block(0, 2);
        var quantity = Row(Question("decimal", "Cantidad"));
        var remito = Question("shortText", "Remito");

        var screens = Screens(Flow(WhatsAppFlowCompiler.Compile(Request(block, quantity, remito))));

        Assert.Equal(new[] { "FORM", "FORM_A", "FORM_B", "FORM_C" }, screens.Select(screen => screen.GetProperty("id").GetString()));
        Assert.Equal("Artículos", screens[0].GetProperty("title").GetString());
        var ask = Controls(screens[0]);
        Assert.Equal(block.Key, ask[0].GetProperty("name").GetString());
        Assert.Equal("¿Agregás un renglón?", ask[0].GetProperty("label").GetString());
        Assert.Equal("FORM_A", Next(ask[1].GetProperty("then")[0]));
        Assert.Equal("FORM_C", Next(ask[1].GetProperty("else")[0]));
        Assert.Equal("If", Controls(screens[1])[^1].GetProperty("type").GetString());
        var payload = Controls(screens[3])[^1].GetProperty("on-click-action").GetProperty("payload");
        Assert.Equal("${screen.FORM.form." + block.Key + "}", payload.GetProperty(block.Key + "_r0").GetString());
        Assert.Equal("${screen.FORM_A.form." + block.Key + "}", payload.GetProperty(block.Key + "_r1").GetString());
        Assert.False(payload.TryGetProperty(block.Key + "_r2", out _));
    }

    [Fact]
    public void WhatsApp_loads_up_to_ten_rows_and_the_last_one_asks_nothing()
    {
        var screens = Screens(Flow(WhatsAppFlowCompiler.Compile(Request(Block(1, 20), Row(Question("decimal"))))));

        Assert.Equal(WhatsAppFlowProfile.MaxRows, screens.Length);
        Assert.Equal(2, Controls(screens[^1]).Length);
    }

    [Fact]
    public void The_rows_of_the_profile_are_the_ones_publishing_warns_about()
    {
        Assert.Equal(StructuredFormLimits.MaxRows, WhatsAppFlowProfile.MaxRows);
    }

    [Fact]
    public void A_block_uses_the_file_budget_and_counts_each_row()
    {
        var article = Row(Question("choice") with { Options = [new(Guid.CreateVersion7(), Text("Urea", "Urea"))] });
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request(Block(1, 10), article, Row(Question("decimal")),
            Row(Question("longText", "Observaciones", 600)))).Issues);

        var notes = Row(Question("longText", "Observaciones", 2000));
        var tooLarge = WhatsAppFlowCompiler.Compile(Request(Block(1, 10), notes));

        Assert.Empty(tooLarge.Flows);
        Assert.Contains(tooLarge.Issues, issue => issue.Error == FlowErrors.ResponseTooLarge && issue.FieldId == notes.FieldId
            && issue.Path == "draft.fields[1].maxLength");
    }

    [Fact]
    public void Components_are_counted_per_screen_in_a_block()
    {
        var children = Enumerable.Range(0, 12).Select(index => Row(Question("decimal", "C" + index))).ToArray();

        var result = WhatsAppFlowCompiler.Compile(Request([Block(1, 10), .. children]));

        Assert.DoesNotContain(result.Issues, issue => issue.Error == FlowErrors.ComponentBudgetExceeded);
        Assert.Equal(2, result.Flows.Count);
    }

    [Fact]
    public void A_malformed_block_is_unsupported()
    {
        var quantity = Row(Question("decimal"));
        var choices = Row(Question("multiChoice") with { Options = [new(Guid.CreateVersion7(), Text("A", "A"))], Selection = new(0, 1) });

        Assert.Contains(WhatsAppFlowCompiler.Compile(Request(quantity)).Issues, Unsupported);
        Assert.Contains(WhatsAppFlowCompiler.Compile(Request(Block(1, 5))).Issues, Unsupported);
        Assert.Contains(WhatsAppFlowCompiler.Compile(Request(Block(1, 5), quantity, Question("date"), Row(Question("date")))).Issues, Unsupported);
        Assert.Contains(WhatsAppFlowCompiler.Compile(Request(Block(1, 5), quantity, Block(1, 5), Row(Question("date")))).Issues, Unsupported);
        Assert.Contains(WhatsAppFlowCompiler.Compile(Request(Block(1, 5) with { Rows = null }, quantity)).Issues, Unsupported);
        Assert.Contains(WhatsAppFlowCompiler.Compile(Request(Block(1, 5), choices)).Issues, Unsupported);
        Assert.Contains(WhatsAppFlowCompiler.Compile(Request(Block(1, 5), quantity with { Rows = new(1, 2) })).Issues, Unsupported);

        static bool Unsupported(StructuredFormIssueResponse issue) => issue.Error == FlowErrors.UnsupportedConfiguration;
    }

    [Fact]
    public void A_catalog_question_in_the_rows_reads_its_options_from_the_first_screen()
    {
        var article = Row(Question("choice", "Artículo") with { SourceCatalogId = Guid.CreateVersion7() });

        var afterSupplier = Screens(Flow(WhatsAppFlowCompiler.Compile(Request(Question("shortText", "Proveedor"), Block(1, 2), article))));
        var first = Screens(Flow(WhatsAppFlowCompiler.Compile(Request(Block(1, 2), article))));

        Assert.Equal("${screen.FORM.data." + article.Key + "_options}", Controls(afterSupplier[1])[0].GetProperty("data-source").GetString());
        Assert.True(afterSupplier[0].GetProperty("data").TryGetProperty(article.Key + "_options", out _));
        Assert.Equal("${data." + article.Key + "_options}", Controls(first[0])[0].GetProperty("data-source").GetString());
        Assert.Equal("${screen.FORM.data." + article.Key + "_options}", Controls(first[1])[0].GetProperty("data-source").GetString());
    }

    [Fact]
    public void The_block_name_titles_its_rows_within_its_own_limit()
    {
        var block = Block(1, 2) with { Label = Text(new string('a', 61), "Items") };

        var result = WhatsAppFlowCompiler.Compile(Request(block, Row(Question("decimal"))));

        Assert.Contains(result.Issues, issue => issue.Error == FlowErrors.TextTooLong && issue.Path == "draft.fields[0].label.es" && issue.Limit == 60);
    }

    [Fact]
    public void Prefill_follows_the_screens_and_leaves_the_block_out()
    {
        var supplier = Question("shortText", "Proveedor");
        var block = Block(1, 2);
        var quantity = Row(Question("decimal"));
        var remito = Question("shortText", "Remito");
        var flow = Flow(WhatsAppFlowCompiler.Compile(Request(supplier, block, quantity, remito)));
        var projection = Projection(flow);

        var result = WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [supplier.Key] = "Agro", [remito.Key] = "0003" });

        Assert.True(result.IsSuccess);
        Assert.Equal("Agro", result.Value["prefill_FORM"][supplier.Key]);
        Assert.Equal("0003", result.Value["prefill_FORM_C"][remito.Key]);
        Assert.True(WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [quantity.Key] = "1" }).IsFailure);
        Assert.True(WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [block.Key] = "true" }).IsFailure);
        var screens = Screens(flow);
        Assert.Equal("${screen.FORM.data.prefill_FORM_C}", screens[3].GetProperty("layout").GetProperty("children")[0].GetProperty("init-values").GetString());
        Assert.False(screens[1].GetProperty("layout").GetProperty("children")[0].TryGetProperty("init-values", out _));
    }

    [Fact]
    public void Meta_diagnostics_on_a_row_name_the_question_of_the_block()
    {
        var supplier = Question("shortText", "Proveedor");
        var block = Block(1, 2);
        var quantity = Row(Question("decimal"));
        var remito = Question("shortText", "Remito");
        var projection = Projection(Flow(WhatsAppFlowCompiler.Compile(Request(supplier, block, quantity, remito))));

        var label = WhatsAppFlowDispatchService.Map(new WhatsAppFlowValidationPointer(0, null, "label", ScreenIndex: 2), projection);
        var question = WhatsAppFlowDispatchService.Map(new WhatsAppFlowValidationPointer(1, null, "label", ScreenIndex: 1), projection);
        var after = WhatsAppFlowDispatchService.Map(new WhatsAppFlowValidationPointer(0, null, "label", ScreenIndex: 3), projection);

        Assert.Equal(quantity.FieldId, label.FieldId);
        Assert.Equal("draft.fields[2].label.es", label.Path);
        Assert.Equal(block.FieldId, question.FieldId);
        Assert.Equal("draft.fields[1]", question.Path);
        Assert.Equal(remito.FieldId, after.FieldId);
    }
}
