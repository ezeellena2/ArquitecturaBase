using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// Task 9b of the documents: r14 opens a form that reads documents with what was read and its marks.
public sealed class WhatsAppFlowDocumentCompilerTests
{
    // Without reading r14 adds nothing: the bytes r13 compiled for a form on several screens, with a condition and a catalog on a later
    // screen (flow-static-7.3-r13-screens, exported from r13), without the flag or with reading off. r15 only changes the help of
    // its decimal (H18).
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Without_reading_r14_compiles_the_bytes_of_r13_for_a_form_on_several_screens(string culture)
    {
        Assert.Equal("flow-static-7.3-r15", WhatsAppFlowCompiler.ProfileRevision);
        var r13 = FlowProfileFixtures.Read($"flow-static-7.3-r13-screens-{culture}");
        Assert.Equal(3, JsonDocument.Parse(r13).RootElement.GetProperty("screens").GetArrayLength());
        foreach (var reads in new bool?[] { null, false })
            Assert.Equal(FlowProfileFixtures.AsR15(r13, culture), WhatsAppFlowCompiler.Compile(Screens() with { ReadsDocuments = reads }).Flows.Single(flow => flow.Culture == culture).Json);
    }

    [Fact]
    public void Reading_adds_the_hidden_notice_the_dynamic_help_and_the_error_messages_of_each_form()
    {
        var request = Request() with { ReadsDocuments = true };
        var result = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(result.Issues);
        using var json = JsonDocument.Parse(result.Flows.Single(flow => flow.Culture == "es").Json);
        var screens = json.RootElement.GetProperty("screens").EnumerateArray().ToArray();
        // The photo, the first one, starts the last screen: it gets its twin (below, for a document).
        Assert.Equal(["FORM", "FORM_A", "FORM_A_ATTACHED"], screens.Select(screen => screen.GetProperty("id").GetString()));
        var form = screens[0].GetProperty("layout").GetProperty("children")[0];
        var children = form.GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal("TextBody", children[0].GetProperty("type").GetString());
        Assert.Equal("${data.notice}", children[0].GetProperty("text").GetString());
        Assert.Equal("${data.notice_visible}", children[0].GetProperty("visible").GetString());
        Assert.Equal("${data.errors_FORM}", form.GetProperty("error-messages").GetString());
        Assert.Equal("${data.prefill_FORM}", form.GetProperty("init-values").GetString());
        var data = screens[0].GetProperty("data");
        Assert.Equal("string", data.GetProperty("notice").GetProperty("type").GetString());
        Assert.False(data.GetProperty("notice_visible").GetProperty("__example__").GetBoolean());
        Assert.Equal("object", data.GetProperty("errors_FORM").GetProperty("type").GetString());
        // The short text and the date have no help of their own; the long text keeps its own, and the number its usual one.
        Assert.Equal("${data." + request.Questions[0].Key + "_help}", Control(children, request.Questions[0].Key).GetProperty("helper-text").GetString());
        Assert.Equal("Ayuda", Control(children, request.Questions[1].Key).GetProperty("helper-text").GetString());
        Assert.DoesNotContain("_help", Control(children, request.Questions[2].Key).GetProperty("helper-text").GetString(), StringComparison.Ordinal);
        Assert.Equal("${data." + request.Questions[3].Key + "_help}", Control(children, request.Questions[3].Key).GetProperty("helper-text").GetString());
        Assert.False(data.TryGetProperty(request.Questions[1].Key + "_help", out _));
        Assert.True(data.TryGetProperty(request.Questions[3].Key + "_help", out _));
        // A later screen reads the data of the first one.
        var later = screens[1].GetProperty("layout").GetProperty("children")[0];
        Assert.Equal("${screen.FORM.data.errors_FORM_A}", later.GetProperty("error-messages").GetString());
        Assert.Equal("${screen.FORM.data." + request.Questions[^1].Key + "_help}",
            Control(later.GetProperty("children").EnumerateArray().ToArray(), request.Questions[^1].Key).GetProperty("helper-text").GetString());
    }

    [Fact]
    public void The_last_question_that_may_receive_the_document_gets_a_twin_screen_chosen_by_an_if()
    {
        var request = Request() with { ReadsDocuments = true, MaxFileSizeMiB = 10, Questions = [.. Request().Questions.Take(4),
            Question("document") with { Required = true, Files = new(1, 1) }] };
        var document = request.Questions[^1];
        var result = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(result.Issues);
        using var json = JsonDocument.Parse(result.Flows.Single(flow => flow.Culture == "es").Json);
        var screens = json.RootElement.GetProperty("screens").EnumerateArray().ToArray();
        Assert.Equal(["FORM", "FORM_A", "FORM_A_ATTACHED"], screens.Select(screen => screen.GetProperty("id").GetString()));
        var routing = json.RootElement.GetProperty("routing_model");
        Assert.Equal(["FORM_A", "FORM_A_ATTACHED"], routing.GetProperty("FORM").EnumerateArray().Select(value => value.GetString()));
        Assert.Empty(routing.GetProperty("FORM_A").EnumerateArray());
        Assert.Empty(routing.GetProperty("FORM_A_ATTACHED").EnumerateArray());
        var branch = screens[0].GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().Last();
        Assert.Equal("If", branch.GetProperty("type").GetString());
        Assert.Equal("${data." + document.Key + "_attached}", branch.GetProperty("condition").GetString());
        Assert.Equal("FORM_A_ATTACHED", branch.GetProperty("then")[0].GetProperty("on-click-action").GetProperty("next").GetProperty("name").GetString());
        Assert.Equal("FORM_A", branch.GetProperty("else")[0].GetProperty("on-click-action").GetProperty("next").GetProperty("name").GetString());
        Assert.False(screens[0].GetProperty("data").GetProperty(document.Key + "_attached").GetProperty("__example__").GetBoolean());
        var normal = screens[1].GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().ToArray();
        var twin = screens[2].GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().ToArray();
        Assert.Equal(1, normal[0].GetProperty("min-uploaded-documents").GetInt32());
        Assert.Equal("TextBody", twin[0].GetProperty("type").GetString());
        Assert.StartsWith("Ya adjuntamos el documento", twin[0].GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, twin[1].GetProperty("min-uploaded-documents").GetInt32());
        Assert.True(screens[1].GetProperty("terminal").GetBoolean());
        Assert.True(screens[2].GetProperty("terminal").GetBoolean());
        // Each twin completes with its own picker and the answers of the first screen.
        foreach (var children in new[] { normal, twin })
        {
            var payload = children[^1].GetProperty("on-click-action").GetProperty("payload");
            Assert.Equal("${form." + document.Key + "}", payload.GetProperty(document.Key).GetString());
            Assert.Equal("${screen.FORM.form." + request.Questions[0].Key + "}", payload.GetProperty(request.Questions[0].Key).GetString());
        }
    }

    [Fact]
    public void Without_a_screen_before_it_or_with_more_screens_after_it_the_document_has_no_twin()
    {
        var document = Question("document") with { Required = true, Files = new(1, 1) };
        var first = WhatsAppFlowCompiler.Compile(Request() with { ReadsDocuments = true, MaxFileSizeMiB = 10, Questions = [document, Request().Questions[0]] });
        Assert.DoesNotContain("_ATTACHED", first.Flows[0].Json, StringComparison.Ordinal);
        var middle = WhatsAppFlowCompiler.Compile(Request() with { ReadsDocuments = true, MaxFileSizeMiB = 10,
            Questions = [Request().Questions[0], document, Question("document") with { Files = new(0, 1) }] });
        Assert.DoesNotContain("_ATTACHED", middle.Flows[0].Json, StringComparison.Ordinal);
        Assert.Empty(middle.Issues);
    }

    // The server adds only the sent file, when the person chooses none: with a twin of no minimum a question that needs two would arrive
    // with one, and the capture would reject it.
    [Fact]
    public void A_document_that_needs_two_files_has_no_twin_because_the_server_adds_only_the_one_sent()
    {
        var two = Request() with { ReadsDocuments = true, MaxFileSizeMiB = 10, Questions = [.. Request().Questions.Take(4),
            Question("document") with { Required = true, Files = new(2, 3) }] };
        var document = two.Questions[^1];
        var result = WhatsAppFlowCompiler.Compile(two);
        Assert.Empty(result.Issues);
        Assert.All(result.Flows, flow => Assert.DoesNotContain("_ATTACHED", flow.Json, StringComparison.Ordinal));
        // The marks of a send read what the Flow declares: without its twin, the sent document chooses no screen.
        var file = new WhatsAppFlowServerFile(document.FieldId, "whatsapp/documents/x.pdf", "x.pdf", "application/pdf", 10, new string('a', 64));
        Assert.DoesNotContain(document.Key + "_attached", WhatsAppFlowMarks.Build(Projection(two), new(file, null)).Flags.Keys);
        // One file completes it even when it allows three: the twin stays.
        var one = two with { Questions = [.. two.Questions.SkipLast(1), document with { Files = new(1, 3) }] };
        Assert.Contains("FORM_A_ATTACHED", WhatsAppFlowCompiler.Compile(one).Flows[0].Json, StringComparison.Ordinal);
    }

    // The Flow does not take a condition on a photo or a document (CO1-A), so a twin never has its picker inside an If.
    [Fact]
    public void A_document_with_a_condition_does_not_compile_so_no_twin_sits_outside_its_if()
    {
        var concept = Question("boolean") with { Required = true };
        var document = Question("document") with { Files = new(0, 1), ShowWhen = new(concept.FieldId, ["true"]) };
        var result = WhatsAppFlowCompiler.Compile(Request() with { ReadsDocuments = true, MaxFileSizeMiB = 10, Questions = [Request().Questions[0], concept, document] });
        Assert.Empty(result.Flows);
        Assert.Contains(result.Issues, issue => issue.Path == "draft.fields[2].showWhen");
    }

    [Fact]
    public void Marks_fill_the_help_the_errors_the_notice_and_the_twin_of_the_send()
    {
        var request = Request() with { ReadsDocuments = true, MaxFileSizeMiB = 10, Questions = [.. Request().Questions,
            Question("choice") with { Options = [new(Guid.CreateVersion7(), Text("Opción"))], Label = Text("Banco") },
            Question("document") with { Files = new(0, 1) }] };
        var projection = Projection(request);
        var bank = request.Questions[^2];
        var file = new WhatsAppFlowServerFile(request.Questions[^1].FieldId, "whatsapp/documents/x.pdf", "x.pdf", "application/pdf", 10, new string('a', 64));
        var server = new WhatsAppFlowServerData(file, [
            new(request.Questions[0].FieldId, StructuredDocumentMarkKind.Doubtful),
            new(request.Questions[3].FieldId, StructuredDocumentMarkKind.NotRead),
            new(request.Questions[1].FieldId, StructuredDocumentMarkKind.Invalid, "Forms.Record.BankAccountInvalid"),
            new(bank.FieldId, StructuredDocumentMarkKind.NotRead)]);
        var data = WhatsAppFlowMarks.Build(projection, server);
        Assert.Equal("Revisá: lo leímos con dudas.", data.Texts[request.Questions[0].Key + "_help"]);
        Assert.Equal("No se leyó.", data.Texts[request.Questions[3].Key + "_help"]);
        Assert.Equal("", data.Texts[request.Questions[^3].Key + "_help"]);
        Assert.Equal("Revisá la cuenta: los números no dan. Copiala del comprobante.", data.Objects["errors_FORM"][request.Questions[1].Key]);
        Assert.True(data.Flags["notice_visible"]);
        Assert.Equal("Lo completamos con el documento que mandaste. Revisá lo marcado antes de confirmar.\n«Banco» no se leyó.", data.Texts["notice"]);
        Assert.True(data.Flags[request.Questions[^1].Key + "_attached"]);

        var load = WhatsAppFlowMarks.Build(projection, new WhatsAppFlowServerData(null, null) { Spreadsheet = true });
        Assert.Equal("Lo completamos con la planilla que mandaste. Revisá lo marcado antes de confirmar.", load.Texts["notice"]);
        Assert.False(load.Flags[request.Questions[^1].Key + "_attached"]);
        Assert.Empty(load.Objects);
        var operatorSend = WhatsAppFlowMarks.Build(projection, null);
        Assert.False(operatorSend.Flags["notice_visible"]);
        Assert.Equal("", operatorSend.Texts["notice"]);
        Assert.Same(WhatsAppFlowDynamicData.Empty, WhatsAppFlowMarks.Build(Projection(request with { ReadsDocuments = null }), server));
    }

    [Fact]
    public void Meta_diagnostics_count_the_notice_atop_the_first_screen_as_no_question()
    {
        var projection = Projection(Request() with { ReadsDocuments = true });
        Assert.Equal("channel", WhatsAppFlowDispatchService.Map(new(0, null, "text"), projection).Path);
        Assert.Equal("draft.fields[0].label.es", WhatsAppFlowDispatchService.Map(new(1, null, "label"), projection).Path);
        Assert.Equal("draft.fields[1].label.es", WhatsAppFlowDispatchService.Map(new(2, null, "label"), projection).Path);
    }

    private static JsonElement Control(JsonElement[] children, string key) => children.Single(child => child.TryGetProperty("name", out var name) && name.GetString() == key);

    private static WhatsAppFlowProjection Projection(StructuredFormCheckRequest request)
    {
        var result = WhatsAppFlowCompiler.Compile(request).Flows.Single(flow => flow.Culture == "es");
        return new(1, result.Json, result.Hash, WhatsAppFlowCompiler.ProfileRevision, 1, result.Culture,
            result.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type, field.Required, field.MaxLength, field.Choices, field.Files)).ToArray());
    }

    // A short text, a long text with its own help, a number, a date and, on its own screen after a photo, another short text.
    private static StructuredFormCheckRequest Request() => new(1, "es", Text("Comprobante"), Text("Enviar"), [
        Question("shortText") with { MaxLength = 80 },
        Question("longText") with { MaxLength = 600, Help = Text("Ayuda") },
        Question("decimal"),
        Question("date"),
        Question("photo") with { Files = new(0, 1, "camera") },
        Question("shortText") with { MaxLength = 40 },
    ], ["es", "en"]);

    // A form without reading on three screens (a photo and a document start theirs), with a condition on the first one and a catalog
    // on a later one: what r13 compiled for it is flow-static-7.3-r13-screens.
    private static StructuredFormCheckRequest Screens()
    {
        static StructuredFormQuestion Fixed(int value, string type, int? maxLength = null)
        {
            var id = new Guid($"019a0000-0000-7000-8000-{value:000000000000}");
            return new(id, "field_" + id.ToString("N"), type, Text("Pregunta"), null, true, maxLength, null);
        }
        var concept = Fixed(2, "boolean");
        return new StructuredFormCheckRequest(1, "es", Text("Comprobante"), Text("Enviar"), [
            Fixed(1, "shortText", 80),
            concept,
            Fixed(3, "shortText", 80) with { Required = false, ShowWhen = new(concept.FieldId, ["true"]) },
            Fixed(4, "photo") with { Required = false, Files = new(0, 2, "camera") },
            Fixed(5, "date"),
            Fixed(6, "choice") with { SourceCatalogId = new Guid("019a0000-0000-7000-8000-000000000200") },
            Fixed(7, "document") with { Files = new(1, 1) },
            Fixed(8, "longText", 600) with { Help = Text("Ayuda") },
            Fixed(9, "decimal"),
        ], ["es", "en"]) with { MaxFileSizeMiB = 10 };
    }

    private static StructuredFormQuestion Question(string type)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text("Pregunta"), null, false, null, null);
    }

    private static Dictionary<string, string> Text(string value) => new() { ["es"] = value, ["en"] = value };
}
