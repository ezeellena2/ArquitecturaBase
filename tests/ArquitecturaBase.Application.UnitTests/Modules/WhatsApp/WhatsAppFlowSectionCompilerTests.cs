using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// Section titles in the Flow (CW15-B): a TextSubheading right before the first question of its section, on that question's
// screen, without new screens. Without titles r10 compiles the same bytes.
public sealed class WhatsAppFlowSectionCompilerTests
{
    private static Dictionary<string, string> Text(string spanish, string english) => new() { ["es"] = spanish, ["en"] = english };

    private static StructuredFormQuestion Question(string type, string label)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text(label, label), null, type == "photo", type is "shortText" or "longText" ? 80 : null, null,
            Files: type == "photo" ? new(1, 3, "cameraOrGallery") : null);
    }

    private static StructuredFormQuestion Block(int minimum) =>
        Question("group", "Artículos") with { Required = minimum > 0, Rows = new(minimum, 3) };

    private static StructuredFormSection Section(int start, string spanish, string english) => new(start, Text(spanish, english));

    private static StructuredFormCheckRequest Request(IReadOnlyList<StructuredFormSection>? sections, params StructuredFormQuestion[] questions) =>
        new(1, "es", Text("Aplicación", "Application"), Text("Enviar", "Send"), questions, ["es", "en"],
            questions.Any(question => question.Files is not null) ? 25 : null, sections);

    private static WhatsAppCompiledFlowResponse Flow(StructuredFormCheckRequest request, string culture = "es")
    {
        var result = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(result.Issues);
        return result.Flows.Single(flow => flow.Culture == culture);
    }

    private static JsonElement[] Screens(WhatsAppCompiledFlowResponse flow)
    {
        using var document = JsonDocument.Parse(flow.Json);
        return document.RootElement.GetProperty("screens").Clone().EnumerateArray().ToArray();
    }

    private static string[] Children(JsonElement screen) =>
        screen.GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray()
            .Select(child => child.GetProperty("type").GetString() == "TextSubheading" ? "# " + child.GetProperty("text").GetString()
                : child.TryGetProperty("name", out var name) ? name.GetString()! : child.GetProperty("type").GetString()!).ToArray();

    private static WhatsAppFlowProjection Projection(WhatsAppCompiledFlowResponse flow) =>
        new(1, flow.Json, flow.Hash, WhatsAppFlowCompiler.ProfileRevision, WhatsAppFlowCompiler.CodecVersion, flow.Culture,
            flow.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type, field.Required, field.MaxLength,
                field.Choices, field.Files, field.Selection, field.Rows, field.Repeated)).ToArray());

    [Fact]
    public void Each_title_is_a_subtitle_right_before_the_first_question_of_its_section()
    {
        StructuredFormQuestion[] questions = [Question("shortText", "Lote"), Question("decimal", "Hectáreas"), Question("shortText", "Producto"), Question("decimal", "Dosis")];
        var request = Request([Section(0, "Lote", "Lot"), Section(2, "Productos", "Products")], questions);

        var spanish = Assert.Single(Screens(Flow(request)));
        var english = Assert.Single(Screens(Flow(request, "en")));

        Assert.Equal(["# Lote", questions[0].Key, questions[1].Key, "# Productos", questions[2].Key, questions[3].Key, "Footer"], Children(spanish));
        Assert.Equal(["# Lot", questions[0].Key, questions[1].Key, "# Products", questions[2].Key, questions[3].Key, "Footer"], Children(english));
    }

    [Fact]
    public void Without_titles_the_flow_keeps_its_bytes()
    {
        StructuredFormQuestion[] questions = [Question("shortText", "Lote"), Question("photo", "Foto")];

        var without = Flow(Request(null, questions));
        var empty = Flow(Request([], questions));

        Assert.Equal(without.Json, empty.Json);
        Assert.Equal(without.Hash, empty.Hash);
        Assert.DoesNotContain("TextSubheading", without.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_title_before_a_photo_goes_on_the_photo_screen()
    {
        var lot = Question("shortText", "Lote");
        var photo = Question("photo", "Fotos del lote");

        var screens = Screens(Flow(Request([Section(1, "Fotos", "Photos")], lot, photo)));

        Assert.Equal([lot.Key, "Footer"], Children(screens[0]));
        Assert.Equal(["# Fotos", photo.Key, "Footer"], Children(screens[1]));
    }

    [Theory]
    [InlineData(1, "FORM_A")]
    [InlineData(0, "FORM_A")]
    public void A_title_before_the_block_goes_on_its_first_screen_and_one_after_it_on_the_next(int minimum, string firstBlockScreen)
    {
        var supplier = Question("shortText", "Proveedor");
        var block = Block(minimum);
        var quantity = Question("decimal", "Cantidad") with { Repeated = true };
        var remito = Question("shortText", "Remito");

        var screens = Screens(Flow(Request([Section(1, "Insumos", "Supplies"), Section(3, "Cierre", "Closing")], supplier, block, quantity, remito)));

        var first = screens.Single(screen => screen.GetProperty("id").GetString() == firstBlockScreen);
        Assert.Equal("# Insumos", Children(first)[0]);
        Assert.Equal(1, screens.Count(screen => Children(screen).Contains("# Insumos")));
        Assert.Equal(["# Cierre", remito.Key, "Footer"], Children(screens[^1]));
    }

    [Fact]
    public void A_title_without_its_translation_or_too_long_is_an_issue_at_its_path()
    {
        var question = Question("shortText", "Lote");
        var missing = WhatsAppFlowCompiler.Compile(Request([new(0, new Dictionary<string, string> { ["es"] = "Lote" })], question));
        var tooLong = WhatsAppFlowCompiler.Compile(Request([Section(0, new string('a', 81), "Lot")], question));

        var issue = Assert.Single(missing.Issues);
        Assert.Equal(("draft.sections[0].title.en", FlowErrors.MissingTranslation.Code), (issue.Path, issue.Error.Code));
        var length = Assert.Single(tooLong.Issues);
        Assert.Equal(("draft.sections[0].title.es", FlowErrors.TextTooLong.Code, 80), (length.Path, length.Error.Code, length.Limit!.Value));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 2)]
    [InlineData(-1, 1)]
    public void A_title_without_questions_below_or_out_of_place_is_unsupported(int first, int second)
    {
        var result = WhatsAppFlowCompiler.Compile(Request([Section(first, "Uno", "One"), Section(second, "Dos", "Two")], Question("shortText", "A"), Question("shortText", "B")));

        var issue = Assert.Single(result.Issues);
        Assert.Equal(("draft.sections", FlowErrors.UnsupportedConfiguration.Code), (issue.Path, issue.Error.Code));
        Assert.Empty(result.Flows);
    }

    [Fact]
    public void A_title_inside_the_block_is_unsupported()
    {
        var result = WhatsAppFlowCompiler.Compile(Request([Section(2, "Adentro", "Inside")], Question("shortText", "Proveedor"), Block(1),
            Question("decimal", "Cantidad") with { Repeated = true }, Question("decimal", "Precio") with { Repeated = true }));

        Assert.Equal("draft.sections", Assert.Single(result.Issues).Path);
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(9, true)]
    public void Subtitles_count_toward_the_components_of_a_screen(int titles, bool exceeded)
    {
        var questions = Enumerable.Range(1, 40).Select(number => Question("shortText", "P" + number)).ToArray();
        var result = WhatsAppFlowCompiler.Compile(Request(Enumerable.Range(0, titles).Select(index => Section(index * 4, "S" + index, "S" + index)).ToArray(), questions));

        Assert.Equal(exceeded, result.Issues.Any(issue => issue.Error.Code == FlowErrors.ComponentBudgetExceeded.Code));
    }

    [Fact]
    public void A_diagnostic_on_a_subtitle_points_to_its_title_and_the_questions_after_it_keep_their_place()
    {
        StructuredFormQuestion[] questions = [Question("shortText", "Lote"), Question("shortText", "Producto")];
        var projection = Projection(Flow(Request([Section(0, "Lote", "Lot"), Section(1, "Productos", "Products")], questions)));

        Assert.Equal("draft.sections[1].title.es", WhatsAppFlowDispatchService.Map(new(2, null, "text"), projection).Path);
        Assert.Equal("draft.fields[1].label.es", WhatsAppFlowDispatchService.Map(new(3, null, "label"), projection).Path);
        Assert.Equal("draft.fields[0].label.es", WhatsAppFlowDispatchService.Map(new(1, null, "label"), projection).Path);
    }
}
