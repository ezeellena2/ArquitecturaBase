using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// CO1-A in the Flow (profile r13): a question with a condition goes inside an If, as the block of rows does with «¿Agregás otro
// ítem?». Meta's validator takes one comparison but refuses «||» and «&&» between two (2026-11-11): one answer compares, several
// answers read a boolean that each option of the question sets with update_data, and a chain nests one If per condition. The
// question keeps its required and its key in the complete payload, it is not prefilled, and a title whose questions share their
// condition shows with them. A file question does not take a condition.
public sealed class WhatsAppFlowConditionCompilerTests
{
    private static readonly Guid Goods = Guid.Parse("019a0000-0000-7000-8000-0000000000a1");
    private static readonly Guid Services = Guid.Parse("019a0000-0000-7000-8000-0000000000a2");
    private static readonly Guid Both = Guid.Parse("019a0000-0000-7000-8000-0000000000a3");

    private static Dictionary<string, string> Text(string spanish, string english) => new() { ["es"] = spanish, ["en"] = english };

    private static StructuredFormQuestion Question(string type, string label = "Pregunta")
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text(label, label), null, false, type is "shortText" or "longText" ? 80 : null, null,
            Files: type is "photo" ? new(0, 1, "camera") : type is "document" ? new(0, 1) : null);
    }

    private static StructuredFormQuestion Concept() => Question("choice", "Concepto") with
    {
        Required = true,
        Options = [new(Goods, Text("Productos", "Goods")), new(Services, Text("Servicios", "Services")), new(Both, Text("Ambos", "Both"))],
    };

    private static StructuredFormQuestion When(StructuredFormQuestion question, StructuredFormQuestion controller, params string[] values) =>
        question with { ShowWhen = new(controller.FieldId, values) };

    private static StructuredFormQuestion Block(int minimum, int maximum) =>
        Question("group") with { Label = Text("Artículos", "Items"), Required = minimum > 0, Rows = new(minimum, maximum) };

    private static StructuredFormCheckRequest Request(params StructuredFormQuestion[] questions) =>
        new(1, "es", Text("Factura", "Invoice"), Text("Enviar", "Send"), questions, ["es", "en"],
            questions.Any(question => question.Files is not null) ? 25 : null);

    private static WhatsAppCompiledFlowResponse Flow(StructuredFormCheckRequest request)
    {
        var result = WhatsAppFlowCompiler.Compile(request);
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

    private static string Type(JsonElement control) => control.GetProperty("type").GetString()!;

    private static string Condition(JsonElement control)
    {
        Assert.Equal("If", Type(control));
        Assert.False(control.TryGetProperty("else", out _));
        return control.GetProperty("condition").GetString()!;
    }

    private static JsonElement Then(JsonElement control) => Assert.Single(control.GetProperty("then").EnumerateArray());

    private static WhatsAppFlowProjection Projection(WhatsAppCompiledFlowResponse flow) =>
        new(1, flow.Json, flow.Hash, WhatsAppFlowCompiler.ProfileRevision, WhatsAppFlowCompiler.CodecVersion, flow.Culture,
            flow.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type, field.Required, field.MaxLength,
                field.Choices, field.Files, field.Selection, field.Rows, field.Repeated, field.ChoosesFirst, field.Calculated, field.ShowWhen)).ToArray());

    [Fact]
    public void A_question_shown_by_several_answers_reads_a_boolean_that_each_option_of_its_question_sets()
    {
        var concept = Concept();
        var since = When(Question("date", "Período desde") with { Required = true }, concept, Services.ToString("D"), Both.ToString("D"));
        var notes = Question("shortText", "Notas");
        var flow = Flow(Request(concept, since, notes));

        var screen = Assert.Single(Screens(flow));
        var controls = Controls(screen);
        Assert.Equal(["Dropdown", "If", "TextInput", "Footer"], controls.Select(Type));
        var shown = since.Key + "_shown";
        Assert.Equal("${data." + shown + "}", Condition(controls[1]));
        var options = controls[0].GetProperty("data-source").EnumerateArray().ToArray();
        Assert.All(options, option => Assert.Equal("update_data", option.GetProperty("on-select-action").GetProperty("name").GetString()));
        Assert.Equal([false, true, true], options.Select(option => option.GetProperty("on-select-action").GetProperty("payload").GetProperty(shown).GetBoolean()));
        var data = screen.GetProperty("data");
        Assert.Equal("boolean", data.GetProperty(shown).GetProperty("type").GetString());
        Assert.False(data.GetProperty(shown).GetProperty("__example__").GetBoolean());
        var date = Then(controls[1]);
        Assert.Equal("CalendarPicker", Type(date));
        Assert.Equal(since.Key, date.GetProperty("name").GetString());
        Assert.True(date.GetProperty("required").GetBoolean());
        var payload = controls[^1].GetProperty("on-click-action").GetProperty("payload");
        Assert.Equal("${form." + since.Key + "}", payload.GetProperty(since.Key).GetString());
        // Neither the question with the condition nor the one whose options set the boolean is prefilled: the Flow opens with the
        // boolean false, so the question opens hidden (CO1-A).
        Assert.Equal([notes.Key], data.GetProperty("prefill_FORM").GetProperty("properties").EnumerateObject().Select(property => property.Name));
        Assert.Equal(new StructuredFormShowWhen(concept.FieldId, [Services.ToString("D"), Both.ToString("D")]).Values, flow.Mapping[1].ShowWhen!.Values);
        Assert.Equal(concept.FieldId, flow.Mapping[1].ShowWhen!.FieldId);
        // The If is one more component.
        Assert.Equal(Flow(Request(concept, since with { ShowWhen = null }, notes)).ComponentCount + 1, flow.ComponentCount);
    }

    [Fact]
    public void A_question_shown_by_yes_or_no_compares_with_true_or_false()
    {
        var discount = Question("boolean", "¿Tiene descuento?");
        var amount = When(Question("decimal", "Descuento"), discount, "true");
        var reason = When(Question("shortText", "Motivo"), discount, "false");

        var controls = Controls(Assert.Single(Screens(Flow(Request(discount, amount, reason)))));

        Assert.Equal("${form." + discount.Key + "} == 'true'", Condition(controls[1]));
        Assert.Equal(amount.Key, Then(controls[1]).GetProperty("name").GetString());
        Assert.Equal("${form." + discount.Key + "} == 'false'", Condition(controls[2]));
    }

    [Fact]
    public void A_question_whose_question_has_a_condition_goes_inside_an_if_nested_in_the_one_of_its_question()
    {
        var service = Question("boolean", "¿Es un servicio?");
        var monthly = When(Question("boolean", "¿Es mensual?"), service, "true");
        var month = When(Question("shortText", "Mes"), monthly, "true");

        var flow = Flow(Request(service, monthly, month));
        var controls = Controls(Assert.Single(Screens(flow)));

        Assert.Equal("${form." + service.Key + "} == 'true'", Condition(controls[2]));
        var inner = Then(controls[2]);
        Assert.Equal("${form." + monthly.Key + "} == 'true'", Condition(inner));
        Assert.Equal(month.Key, Then(inner).GetProperty("name").GetString());
        // Each If is a component.
        Assert.Equal(Flow(Request(service, monthly, month with { ShowWhen = null })).ComponentCount + 2, flow.ComponentCount);
    }

    [Fact]
    public void A_chain_reads_the_boolean_of_its_question_when_that_one_is_shown_by_several_answers()
    {
        var concept = Concept();
        var discount = When(Question("boolean", "¿Tiene descuento?"), concept, Goods.ToString("D"), Both.ToString("D"));
        var amount = When(Question("decimal", "Descuento"), discount, "true");

        var controls = Controls(Assert.Single(Screens(Flow(Request(concept, discount, amount)))));

        Assert.Equal(["Dropdown", "If", "If", "Footer"], controls.Select(Type));
        Assert.Equal("${data." + discount.Key + "_shown}", Condition(controls[1]));
        Assert.Equal("${data." + discount.Key + "_shown}", Condition(controls[2]));
        var inner = Then(controls[2]);
        Assert.Equal("${form." + discount.Key + "} == 'true'", Condition(inner));
        Assert.Equal(amount.Key, Then(inner).GetProperty("name").GetString());
        // The yes or no keeps its answer while hidden; the inner If shows the amount again when the concept shows it again.
        Assert.False(controls[1].GetProperty("then")[0].GetProperty("data-source")[0].TryGetProperty("on-select-action", out _));
    }

    [Fact]
    public void A_boolean_travels_to_the_screen_of_its_question_through_every_navigate_and_starts_false_on_a_later_screen()
    {
        var concept = Concept();
        var photo = Question("photo", "Foto");
        var period = When(Question("date", "Período"), concept, Services.ToString("D"), Both.ToString("D"));
        var shown = period.Key + "_shown";

        var screens = Screens(Flow(Request(concept, photo, period)));

        Assert.True(screens[0].GetProperty("data").TryGetProperty(shown, out _));
        Assert.Equal("${data." + shown + "}", Controls(screens[0])[^1].GetProperty("on-click-action").GetProperty("payload").GetProperty(shown).GetString());
        Assert.Equal("boolean", screens[1].GetProperty("data").GetProperty(shown).GetProperty("type").GetString());
        Assert.Equal("${data." + shown + "}", Condition(Controls(screens[1])[1]));

        // With the block in the middle every screen up to the one after it declares and passes the boolean, and the screen of the
        // question whose options set it gets it false from a screen before.
        var block = Block(0, 2);
        var amount = Question("decimal", "Cantidad") with { Repeated = true };
        var rows = Screens(Flow(Request(Question("shortText", "Cliente"), photo, concept, block, amount, period)));
        Assert.Equal(["FORM", "FORM_A", "FORM_B", "FORM_C", "FORM_D", "FORM_E"], rows.Select(screen => screen.GetProperty("id").GetString()));
        Assert.False(rows[0].TryGetProperty("data", out var first) && first.TryGetProperty(shown, out _));
        Assert.All(rows[1..], screen => Assert.True(screen.GetProperty("data").TryGetProperty(shown, out _)));
        var navigates = rows.SelectMany(Navigates).ToArray();
        Assert.Contains(navigates, navigate => navigate.Next == "FORM_A" && navigate.Payload.GetProperty(shown).ValueKind == JsonValueKind.False);
        Assert.All(navigates.Where(navigate => navigate.Next != "FORM_A"), navigate => Assert.Equal("${data." + shown + "}", navigate.Payload.GetProperty(shown).GetString()));
        Assert.Equal("${data." + shown + "}", Condition(Controls(rows[^1])[0]));
    }

    [Fact]
    public void A_boolean_also_travels_to_the_screen_of_a_question_further_down_its_chain()
    {
        var concept = Concept();
        var discount = When(Question("boolean", "¿Tiene descuento?"), concept, Goods.ToString("D"), Both.ToString("D"));
        var photo = Question("photo", "Foto");
        var reason = When(Question("shortText", "Motivo"), discount, "true");
        var shown = discount.Key + "_shown";

        var screens = Screens(Flow(Request(concept, discount, photo, reason)));

        // Meta asked for it on 2026-11-11: «Missing dynamic data … in the data model for screen FORM_A».
        Assert.Equal("boolean", screens[1].GetProperty("data").GetProperty(shown).GetProperty("type").GetString());
        Assert.Equal("${data." + shown + "}", Controls(screens[0])[^1].GetProperty("on-click-action").GetProperty("payload").GetProperty(shown).GetString());
        var guard = Controls(screens[1])[1];
        Assert.Equal("${data." + shown + "}", Condition(guard));
        Assert.Equal("${screen.FORM.form." + discount.Key + "} == 'true'", Condition(Then(guard)));
    }

    // Every navigate of a screen, also inside the branches of an If.
    private static IEnumerable<(string Next, JsonElement Payload)> Navigates(JsonElement screen)
    {
        var found = new List<(string, JsonElement)>();
        void Walk(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "navigate"
                    && element.TryGetProperty("next", out var next))
                    found.Add((next.GetProperty("name").GetString()!, element.GetProperty("payload").Clone()));
                foreach (var property in element.EnumerateObject()) Walk(property.Value);
            }
            else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) Walk(item);
        }
        Walk(screen.GetProperty("layout"));
        return found;
    }

    [Fact]
    public void A_question_on_a_later_screen_reads_the_answer_from_the_screen_of_its_question()
    {
        var service = Question("boolean", "¿Es un servicio?");
        var photo = Question("photo", "Foto");
        var period = When(Question("date", "Período"), service, "true");
        var rowsForm = Request(service, Block(1, 2), Question("decimal", "Cantidad") with { Repeated = true }, period);

        var screens = Screens(Flow(Request(service, photo, period)));
        var afterRows = Screens(Flow(rowsForm));

        Assert.Equal("${screen.FORM.form." + service.Key + "} == 'true'", Condition(Controls(screens[1])[1]));
        Assert.Equal("${screen.FORM.form." + service.Key + "} == 'true'", Condition(Controls(afterRows[^1])[0]));
    }

    [Fact]
    public void A_catalog_question_that_chooses_first_puts_each_of_its_dropdowns_inside_the_if()
    {
        var concept = Question("choice", "Rubro") with { SourceCatalogId = Guid.CreateVersion7() };
        var value = Guid.CreateVersion7().ToString("D");
        var article = When(Question("choice", "Artículo") with { SourceCatalogId = Guid.CreateVersion7(), ChoosesFirst = true }, concept, value);

        var controls = Controls(Assert.Single(Screens(Flow(Request(concept, article)))));

        Assert.Equal(["Dropdown", "If", "If", "Footer"], controls.Select(Type));
        Assert.Equal(article.Key + "_group", Then(controls[1]).GetProperty("name").GetString());
        Assert.Equal(article.Key, Then(controls[2]).GetProperty("name").GetString());
        Assert.All(controls[1..3], control => Assert.Equal("${form." + concept.Key + "} == '" + value + "'", Condition(control)));
    }

    [Fact]
    public void A_title_shows_with_its_questions_only_when_they_all_share_one_condition()
    {
        var concept = Concept();
        var since = When(Question("date", "Desde"), concept, Services.ToString("D"), Both.ToString("D"));
        var until = When(Question("date", "Hasta"), concept, Services.ToString("D"), Both.ToString("D"));
        var notes = Question("shortText", "Notas");
        StructuredFormSection[] sections = [new(1, Text("Período", "Period")), new(3, Text("Otros", "Other"))];
        var request = Request(concept, since, until, notes) with { Sections = sections };

        var controls = Controls(Assert.Single(Screens(Flow(request))));

        Assert.Equal(["Dropdown", "If", "If", "If", "TextSubheading", "TextInput", "Footer"], controls.Select(Type));
        Assert.Equal("${data." + since.Key + "_shown}", Condition(controls[1]));
        Assert.Equal("Período", Then(controls[1]).GetProperty("text").GetString());
        // Meta cannot hear «any of them»: with different conditions the title always shows.
        var mixed = Request(concept, since, When(until, concept, Both.ToString("D")), notes) with { Sections = sections };
        Assert.Equal(["Dropdown", "TextSubheading", "If", "If", "TextSubheading", "TextInput", "Footer"],
            Controls(Assert.Single(Screens(Flow(mixed)))).Select(Type));
        // A title inside the Ifs of a chain is still a title for the diagnostics.
        var service = Question("boolean", "¿Es un servicio?");
        var monthly = When(Question("boolean", "¿Es mensual?"), service, "true");
        var month = When(Question("shortText", "Mes"), monthly, "true");
        var chained = Projection(Flow(Request(service, monthly, month) with { Sections = [new(2, Text("Mes", "Month"))] }));
        Assert.Equal("draft.sections[0].title.es", WhatsAppFlowDispatchService.Map(new(2, null, "text"), chained).Path);
        // A section with a question that always shows keeps its plain subtitle, and a diagnostic on the guarded one points to it.
        var projection = Projection(Flow(request));
        Assert.Equal("draft.sections[0].title.es", WhatsAppFlowDispatchService.Map(new(1, null, "text"), projection).Path);
        Assert.Equal("draft.fields[3].label.es", WhatsAppFlowDispatchService.Map(new(5, null, "label"), projection).Path);
        Assert.Equal("draft.fields[1].label.es", WhatsAppFlowDispatchService.Map(new(2, null, "label"), projection).Path);
    }

    [Fact]
    public void A_question_with_a_condition_takes_no_prefilled_value()
    {
        var discount = Question("boolean", "¿Tiene descuento?");
        var amount = When(Question("decimal", "Descuento"), discount, "true");
        var projection = Projection(Flow(Request(discount, amount)));

        Assert.True(WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [discount.Key] = "true" }).IsSuccess);
        Assert.Equal(FlowErrors.InvalidValue, WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [amount.Key] = "10" }).Error);
        // A question whose options set a boolean opens empty: its proposed value is left out, and the rest still goes.
        var concept = Concept();
        var since = When(Question("date", "Desde"), concept, Services.ToString("D"), Both.ToString("D"));
        var notes = Question("shortText", "Notas");
        var withBoolean = Projection(Flow(Request(concept, since, notes)));
        var prefill = WhatsAppFlowPrefillPolicy.Build(withBoolean, new Dictionary<string, string> { [concept.Key] = Services.ToString("D"), [notes.Key] = "Hola" });
        Assert.Equal([notes.Key], Assert.Single(prefill.Value).Value.Keys);
    }

    public static TheoryData<string> Unsupported => new()
    {
        "photo", "document", "later", "repeated", "inBlock", "multiChoice", "otherOption", "notTrue", "noValues", "unknown",
        "catalogValues", "tooDeep",
    };

    // A photo or a document would put its picker inside an If, which the Flow does not need yet. The options of a catalog arrive
    // with the message, so they cannot set a boolean: a catalog question shows another for one of its answers. Meta nests up to
    // three If, so a chain goes three questions deep. Publishing with WhatsApp stops at the question, like any other condition the
    // channel cannot compile.
    [Theory]
    [MemberData(nameof(Unsupported))]
    public void A_condition_the_flow_cannot_show_stops_at_its_question(string kind)
    {
        var concept = Concept();
        var discount = Question("boolean", "¿Tiene descuento?");
        var many = Question("multiChoice", "Varias") with { Options = [new(Guid.CreateVersion7(), Text("A", "A"))], Selection = new(0, 1) };
        var block = Block(1, 2);
        var repeated = Question("decimal", "Cantidad") with { Repeated = true };
        var target = Question(kind is "photo" or "document" ? kind : "shortText", "Dato");
        var catalog = Question("choice", "Rubro") with { SourceCatalogId = Guid.CreateVersion7() };
        var second = Question("boolean", "Segunda");
        var third = Question("boolean", "Tercera");
        var fourth = Question("boolean", "Cuarta");
        StructuredFormQuestion[] questions = kind switch
        {
            "later" => [concept, When(target, discount, "true"), discount],
            "repeated" => [discount, block, When(repeated, discount, "true")],
            "inBlock" => [block, repeated, When(target, repeated, "1")],
            "multiChoice" => [many, When(target, many, many.Options![0].ChoiceId.ToString("D"))],
            "otherOption" => [concept, When(target, concept, Guid.CreateVersion7().ToString("D"))],
            "notTrue" => [discount, When(target, discount, "yes")],
            "noValues" => [discount, When(target, discount)],
            "unknown" => [concept, target with { ShowWhen = new(Guid.CreateVersion7(), ["true"]) }],
            "catalogValues" => [catalog, When(target, catalog, Guid.CreateVersion7().ToString("D"), Guid.CreateVersion7().ToString("D"))],
            "tooDeep" => [discount, When(second, discount, "true"), When(third, second, "true"), When(fourth, third, "true"), When(target, fourth, "true")],
            _ => [discount, When(target, discount, "true")],
        };
        var index = kind == "tooDeep" ? questions.Length - 1 : Array.FindIndex(questions, question => question.ShowWhen is not null);

        var result = WhatsAppFlowCompiler.Compile(Request(questions));

        Assert.Empty(result.Flows);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FlowErrors.UnsupportedConfiguration, issue.Error);
        Assert.Equal(questions[index].FieldId, issue.FieldId);
        Assert.Equal($"draft.fields[{index}].showWhen", issue.Path);
    }
}
