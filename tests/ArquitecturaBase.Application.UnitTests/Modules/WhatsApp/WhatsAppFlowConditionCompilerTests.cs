using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// CO1-A in the Flow (profile r13): a question with a condition goes inside an If on the answer of its question, as the block of
// rows does with «¿Agregás otro ítem?». It keeps its required and its key in the complete payload, it is not prefilled, and a
// title whose questions may all be hidden shows with them. A file question does not take a condition.
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
    public void A_question_shown_by_a_choice_goes_inside_an_if_on_its_answers_and_keeps_its_key_and_required()
    {
        var concept = Concept();
        var since = When(Question("date", "Período desde") with { Required = true }, concept, Services.ToString("D"), Both.ToString("D"));
        var notes = Question("shortText", "Notas");
        var flow = Flow(Request(concept, since, notes));

        var screen = Assert.Single(Screens(flow));
        var controls = Controls(screen);
        Assert.Equal(["Dropdown", "If", "TextInput", "Footer"], controls.Select(Type));
        Assert.Equal($"(${{form.{concept.Key}}} == '{Services:D}' || ${{form.{concept.Key}}} == '{Both:D}')", Condition(controls[1]));
        var date = Then(controls[1]);
        Assert.Equal("CalendarPicker", Type(date));
        Assert.Equal(since.Key, date.GetProperty("name").GetString());
        Assert.True(date.GetProperty("required").GetBoolean());
        var payload = controls[^1].GetProperty("on-click-action").GetProperty("payload");
        Assert.Equal("${form." + since.Key + "}", payload.GetProperty(since.Key).GetString());
        // A question with a condition is never prefilled (CO1-A).
        Assert.Equal([concept.Key, notes.Key], screen.GetProperty("data").GetProperty("prefill_FORM").GetProperty("properties")
            .EnumerateObject().Select(property => property.Name));
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
    public void A_question_whose_question_has_a_condition_shows_only_when_both_hold()
    {
        var service = Question("boolean", "¿Es un servicio?");
        var monthly = When(Question("boolean", "¿Es mensual?"), service, "true");
        var month = When(Question("shortText", "Mes"), monthly, "true");

        var controls = Controls(Assert.Single(Screens(Flow(Request(service, monthly, month)))));

        Assert.Equal("${form." + service.Key + "} == 'true' && ${form." + monthly.Key + "} == 'true'", Condition(controls[2]));
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
    public void A_title_whose_questions_may_all_be_hidden_shows_with_any_of_them()
    {
        var concept = Concept();
        var since = When(Question("date", "Desde"), concept, Services.ToString("D"));
        var until = When(Question("date", "Hasta"), concept, Both.ToString("D"));
        var notes = Question("shortText", "Notas");
        var request = Request(concept, since, until, notes) with
        {
            Sections = [new(1, Text("Período", "Period")), new(3, Text("Otros", "Other"))],
        };

        var controls = Controls(Assert.Single(Screens(Flow(request))));

        Assert.Equal(["Dropdown", "If", "If", "If", "TextSubheading", "TextInput", "Footer"], controls.Select(Type));
        Assert.Equal($"(${{form.{concept.Key}}} == '{Services:D}') || (${{form.{concept.Key}}} == '{Both:D}')", Condition(controls[1]));
        Assert.Equal("Período", Then(controls[1]).GetProperty("text").GetString());
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
    }

    public static TheoryData<string> Unsupported => new()
    {
        "photo", "document", "later", "repeated", "inBlock", "multiChoice", "otherOption", "notTrue", "noValues", "unknown",
    };

    // A photo or a document would put its picker inside an If, which the Flow does not need yet: publishing with WhatsApp stops
    // at the question, like any other condition the channel cannot compile.
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
            _ => [discount, When(target, discount, "true")],
        };
        var index = Array.FindIndex(questions, question => question.ShowWhen is not null);

        var result = WhatsAppFlowCompiler.Compile(Request(questions));

        Assert.Empty(result.Flows);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(FlowErrors.UnsupportedConfiguration, issue.Error);
        Assert.Equal(questions[index].FieldId, issue.FieldId);
        Assert.Equal($"draft.fields[{index}].showWhen", issue.Path);
    }
}
