using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// LD2-A (2026-11-07), profile r11: a catalog question that chooses first by a field shows a dropdown with the values before its own
// list, which starts hidden and empty and is filled by the update_data of the value chosen. The values, their label and their
// options come with the message, so the compiled Flow does not depend on the field's name.
public sealed class WhatsAppFlowChooseFirstCompilerTests
{
    private static Dictionary<string, string> Text(string spanish, string english) => new() { ["es"] = spanish, ["en"] = english };

    private static StructuredFormQuestion Question(string type, string label = "Pregunta")
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text(label, label), null, false, type is "shortText" or "longText" ? 80 : null, null);
    }

    private static StructuredFormQuestion Grouped(string label, bool required) =>
        Question("choice", label) with { SourceCatalogId = Guid.CreateVersion7(), Required = required, ChoosesFirst = true };

    private static StructuredFormQuestion Block(int minimum, int maximum) =>
        Question("group") with { Label = Text("Artículos", "Items"), Required = minimum > 0, Rows = new(minimum, maximum) };

    private static StructuredFormCheckRequest Request(params StructuredFormQuestion[] questions) =>
        new(1, "es", Text("Pedido", "Order"), Text("Enviar", "Send"), questions, ["es", "en"]);

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

    private static JsonElement Action(JsonElement footer) => footer.GetProperty("on-click-action");

    private static WhatsAppFlowProjection Projection(WhatsAppCompiledFlowResponse flow) =>
        new(1, flow.Json, flow.Hash, WhatsAppFlowCompiler.ProfileRevision, WhatsAppFlowCompiler.CodecVersion, flow.Culture,
            flow.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type, field.Required, field.MaxLength,
                field.Choices, field.Files, field.Selection, field.Rows, field.Repeated, field.ChoosesFirst)).ToArray());

    // The list of a question waits empty and hidden; entering its screen starts it that way again.
    private static void AssertEmptyList(JsonElement payload, string key)
    {
        Assert.Empty(payload.GetProperty(key + "_options").EnumerateArray());
        Assert.False(payload.GetProperty(key + "_visible").GetBoolean());
    }

    [Fact]
    public void A_question_that_chooses_first_puts_a_dropdown_of_values_before_its_own_hidden_and_empty_list()
    {
        var article = Grouped("Artículo", required: true);
        var flow = Flow(WhatsAppFlowCompiler.Compile(Request(article)));

        var screen = Assert.Single(Screens(flow));
        var controls = Controls(screen);
        Assert.Equal(["Dropdown", "Dropdown", "Footer"], controls.Select(control => control.GetProperty("type").GetString()));
        var values = controls[0];
        Assert.Equal(article.Key + "_group", values.GetProperty("name").GetString());
        Assert.Equal("${data." + article.Key + "_group_label}", values.GetProperty("label").GetString());
        Assert.Equal("${data." + article.Key + "_groups}", values.GetProperty("data-source").GetString());
        Assert.True(values.GetProperty("required").GetBoolean());
        var own = controls[1];
        Assert.Equal(article.Key, own.GetProperty("name").GetString());
        Assert.Equal("${data." + article.Key + "_options}", own.GetProperty("data-source").GetString());
        Assert.Equal("${data." + article.Key + "_visible}", own.GetProperty("visible").GetString());
        // Meta does not take a required dropdown that is hidden: it is required while it shows.
        Assert.Equal("${data." + article.Key + "_visible}", own.GetProperty("required").GetString());
        var data = screen.GetProperty("data");
        Assert.Equal("array", data.GetProperty(article.Key + "_groups").GetProperty("type").GetString());
        Assert.Equal("string", data.GetProperty(article.Key + "_group_label").GetProperty("type").GetString());
        Assert.Equal("array", data.GetProperty(article.Key + "_options").GetProperty("type").GetString());
        Assert.False(data.GetProperty(article.Key + "_visible").GetProperty("__example__").GetBoolean());
        // Its list is empty until a value is chosen, so it is never prefilled; and only the option travels when the form completes.
        Assert.False(data.TryGetProperty("prefill_FORM", out _));
        Assert.Equal([article.Key], Action(controls[2]).GetProperty("payload").EnumerateObject().Select(property => property.Name));
        Assert.True(Assert.Single(flow.Mapping).ChoosesFirst);
        Assert.Equal(4, flow.ComponentCount);
    }

    [Fact]
    public void An_optional_question_keeps_both_dropdowns_optional()
    {
        var article = Grouped("Artículo", required: false);

        var controls = Controls(Assert.Single(Screens(Flow(WhatsAppFlowCompiler.Compile(Request(article))))));

        Assert.False(controls[0].GetProperty("required").GetBoolean());
        Assert.False(controls[1].GetProperty("required").GetBoolean());
        Assert.Equal("${data." + article.Key + "_visible}", controls[1].GetProperty("visible").GetString());
    }

    [Fact]
    public void On_a_later_screen_the_values_come_from_the_first_and_the_navigate_passes_an_empty_list()
    {
        var supplier = Question("shortText", "Proveedor");
        var photo = Question("photo", "Foto") with { Files = new(0, 1, "camera") };
        var article = Grouped("Artículo", required: true);

        var screens = Screens(Flow(WhatsAppFlowCompiler.Compile(Request(supplier, photo, article))));

        var footer = Controls(screens[0])[^1];
        Assert.Equal("FORM_A", Action(footer).GetProperty("next").GetProperty("name").GetString());
        AssertEmptyList(Action(footer).GetProperty("payload"), article.Key);
        var later = Controls(screens[1]);
        Assert.Equal("${screen.FORM.data." + article.Key + "_groups}", later[1].GetProperty("data-source").GetString());
        Assert.Equal("${screen.FORM.data." + article.Key + "_group_label}", later[1].GetProperty("label").GetString());
        Assert.Equal("${data." + article.Key + "_options}", later[2].GetProperty("data-source").GetString());
        Assert.True(screens[0].GetProperty("data").TryGetProperty(article.Key + "_groups", out _));
        var data = screens[1].GetProperty("data");
        Assert.True(data.TryGetProperty(article.Key + "_options", out _));
        Assert.True(data.TryGetProperty(article.Key + "_visible", out _));
        Assert.False(data.TryGetProperty(article.Key + "_groups", out _));
    }

    [Fact]
    public void Each_row_declares_its_own_list_and_the_navigate_into_it_passes_it_empty()
    {
        var supplier = Question("shortText", "Proveedor");
        var article = Grouped("Artículo", required: true) with { Repeated = true };

        var screens = Screens(Flow(WhatsAppFlowCompiler.Compile(Request(supplier, Block(1, 2), article))));

        AssertEmptyList(Action(Controls(screens[0])[^1]).GetProperty("payload"), article.Key);
        var first = Controls(screens[1]);
        Assert.Equal("${screen.FORM.data." + article.Key + "_groups}", first[0].GetProperty("data-source").GetString());
        Assert.Equal("${data." + article.Key + "_options}", first[1].GetProperty("data-source").GetString());
        // «¿Agregás otro ítem?» goes on to the second row, which starts its list empty.
        var next = first[^1].GetProperty("then")[0];
        Assert.Equal("FORM_B", Action(next).GetProperty("next").GetProperty("name").GetString());
        AssertEmptyList(Action(next).GetProperty("payload"), article.Key);
        foreach (var row in screens.Skip(1))
        {
            Assert.True(row.GetProperty("data").TryGetProperty(article.Key + "_options", out _));
            Assert.True(row.GetProperty("data").TryGetProperty(article.Key + "_visible", out _));
        }
    }

    [Fact]
    public void The_values_dropdown_counts_toward_the_fifty_components()
    {
        var fits = WhatsAppFlowCompiler.Compile(Request([.. Enumerable.Range(0, 24).Select(_ => Grouped("Artículo", required: false))]));
        var exceeds = WhatsAppFlowCompiler.Compile(Request([.. Enumerable.Range(0, 25).Select(_ => Grouped("Artículo", required: false))]));

        Assert.Empty(fits.Issues);
        Assert.Contains(exceeds.Issues, issue => issue.Error == FlowErrors.ComponentBudgetExceeded);
    }

    [Theory]
    [InlineData("multiChoice")]
    [InlineData("ownOptions")]
    public void Choosing_first_is_only_for_a_single_choice_from_a_catalog(string kind)
    {
        var question = kind == "multiChoice"
            ? Grouped("Artículos", required: false) with { Type = "multiChoice", Selection = new(0, 2) }
            : Question("choice", "Artículo") with { Options = [new(Guid.CreateVersion7(), Text("Uno", "One"))], ChoosesFirst = true };

        var result = WhatsAppFlowCompiler.Compile(Request(question));

        Assert.Contains(result.Issues, issue => issue.Error == FlowErrors.UnsupportedConfiguration && issue.FieldId == question.FieldId);
    }

    [Fact]
    public void Meta_diagnostics_on_the_values_dropdown_or_after_it_name_their_question()
    {
        var supplier = Question("shortText", "Proveedor");
        var article = Grouped("Artículo", required: true);
        var remito = Question("shortText", "Remito");
        var projection = Projection(Flow(WhatsAppFlowCompiler.Compile(Request(supplier, article, remito))));

        var values = WhatsAppFlowDispatchService.Map(new WhatsAppFlowValidationPointer(1, null, "label", ScreenIndex: 0), projection);
        var own = WhatsAppFlowDispatchService.Map(new WhatsAppFlowValidationPointer(2, null, "label", ScreenIndex: 0), projection);
        var after = WhatsAppFlowDispatchService.Map(new WhatsAppFlowValidationPointer(3, null, "label", ScreenIndex: 0), projection);

        Assert.Equal((article.FieldId, "draft.fields[1]"), (values.FieldId, values.Path));
        Assert.Equal((article.FieldId, "draft.fields[1].label.es"), (own.FieldId, own.Path));
        Assert.Equal((remito.FieldId, "draft.fields[2].label.es"), (after.FieldId, after.Path));
    }

    [Fact]
    public void Prefill_leaves_a_question_that_chooses_first_out()
    {
        var supplier = Question("shortText", "Proveedor");
        var article = Grouped("Artículo", required: true);
        var flow = Flow(WhatsAppFlowCompiler.Compile(Request(supplier, article)));
        var projection = Projection(flow);

        var prefilled = WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [supplier.Key] = "Agro" });

        Assert.Equal("Agro", prefilled.Value["prefill_FORM"][supplier.Key]);
        Assert.True(WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [article.Key] = Guid.NewGuid().ToString("D") }).IsFailure);
        Assert.Equal([supplier.Key], Screens(flow)[0].GetProperty("data").GetProperty("prefill_FORM").GetProperty("properties").EnumerateObject().Select(property => property.Name));
    }
}
