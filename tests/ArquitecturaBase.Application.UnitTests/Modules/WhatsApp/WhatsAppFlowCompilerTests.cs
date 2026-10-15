using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowCompilerTests
{
    [Fact]
    public void Six_types_compile_in_both_cultures_without_defaults_or_an_endpoint()
    {
        var result = WhatsAppFlowCompiler.Compile(Request());
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Flows.Count);
        foreach (var flow in result.Flows)
        {
            using var json = JsonDocument.Parse(flow.Json);
            var root = json.RootElement;
            Assert.Equal("7.3", root.GetProperty("version").GetString());
            Assert.False(root.TryGetProperty("data_api_version", out _));
            var screen = Assert.Single(root.GetProperty("screens").EnumerateArray());
            Assert.True(screen.GetProperty("terminal").GetBoolean());
            var form = Assert.Single(screen.GetProperty("layout").GetProperty("children").EnumerateArray());
            Assert.False(form.TryGetProperty("init-values", out _));
            var controls = form.GetProperty("children").EnumerateArray().ToArray();
            Assert.Equal(new[] { "TextInput", "TextArea", "TextInput", "CalendarPicker", "RadioButtonsGroup", "Dropdown", "Footer" },
                controls.Select(item => item.GetProperty("type").GetString()));
            Assert.Equal(80, controls[0].GetProperty("max-chars").GetInt32());
            Assert.Equal(600, controls[1].GetProperty("max-length").GetInt32());
            Assert.Equal(WhatsAppFlowValueCodec.DecimalMaxChars, controls[2].GetProperty("max-chars").GetInt32());
            Assert.Equal("text", controls[2].GetProperty("input-type").GetString());
            Assert.True(controls[2].TryGetProperty("pattern", out _));
            Assert.Equal("single", controls[3].GetProperty("mode").GetString());
            Assert.True(controls[3].GetProperty("required").GetBoolean());
            Assert.False(controls[4].TryGetProperty("init-value", out _));
            Assert.Equal(new[] { "true", "false" }, controls[4].GetProperty("data-source").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
            Assert.Equal("complete", controls[^1].GetProperty("on-click-action").GetProperty("name").GetString());
            Assert.Equal(6, flow.Mapping.Count);
        }
    }

    [Fact]
    public void Maps_are_order_independent_but_question_order_and_presentation_change_the_wire_hash()
    {
        var request = Request();
        var first = WhatsAppFlowCompiler.Compile(request);
        var reorderedMaps = request with { SubmitLabel = new Dictionary<string, string> { ["en"] = "Submit", ["es"] = "Enviar" } };
        Assert.Equal(first.Flows[0].Json, WhatsAppFlowCompiler.Compile(reorderedMaps).Flows[0].Json);
        Assert.NotEqual(first.Flows[0].Hash, WhatsAppFlowCompiler.Compile(request with { Questions = request.Questions.Reverse().ToArray() }).Flows[0].Hash);
        Assert.NotEqual(first.Flows[0].Hash, WhatsAppFlowCompiler.Compile(request with { SubmitLabel = Text("Confirmar") }).Flows[0].Hash);
    }

    [Fact]
    public void Catalog_choice_compiles_to_a_dropdown_with_recipient_data_source()
    {
        var question = Question(7, "choice") with { Options = null, SourceCatalogId = Guid.NewGuid() };
        var result = WhatsAppFlowCompiler.Compile(Request() with { Questions = [question] });

        Assert.Empty(result.Issues);
        var flow = result.Flows[0];
        var control = JsonDocument.Parse(flow.Json).RootElement
            .GetProperty("screens")[0].GetProperty("layout").GetProperty("children")[0].GetProperty("children")[0];
        Assert.Equal("Dropdown", control.GetProperty("type").GetString());
        Assert.Equal("${data." + question.Key + "_options}", control.GetProperty("data-source").GetString());
        var source = flow.Json;
        using var document = JsonDocument.Parse(source);
        var options = document.RootElement.GetProperty("screens")[0].GetProperty("data").GetProperty(question.Key + "_options");
        Assert.Equal("array", options.GetProperty("type").GetString());
        var item = options.GetProperty("items");
        Assert.Equal("string", item.GetProperty("properties").GetProperty("id").GetProperty("type").GetString());
        Assert.Equal("string", item.GetProperty("properties").GetProperty("title").GetProperty("type").GetString());
        Assert.Equal("string", item.GetProperty("properties").GetProperty("description").GetProperty("type").GetString());
        Assert.Empty(options.GetProperty("__example__").EnumerateArray());
        Assert.Empty(Assert.Single(flow.Mapping).Choices);
    }

    [Fact]
    public void Too_long_labels_and_choice_titles_are_identity_errors_and_never_clipped()
    {
        var request = Request();
        var questions = request.Questions.ToArray();
        questions[0] = questions[0] with { Label = Text(new string('a', 21)) };
        questions[5] = questions[5] with { Options = [new(ChoiceId, Text(new string('b', 31)))] };
        var result = WhatsAppFlowCompiler.Compile(request with { Questions = questions });
        Assert.Empty(result.Flows);
        Assert.Contains(result.Issues, x => x.FieldId == questions[0].FieldId && x.Path == "draft.fields[0].label.es");
        Assert.Contains(result.Issues, x => x.ChoiceId == ChoiceId && x.Path == "draft.fields[5].options[0].label.es");
        Assert.Equal(new string('a', 21), questions[0].Label["es"]);
    }

    [Fact]
    public void Unsupported_help_format_and_missing_translation_fail_closed()
    {
        var request = Request();
        var questions = request.Questions.ToArray();
        questions[5] = questions[5] with { Help = Text("Ayuda") };
        Assert.NotEmpty(WhatsAppFlowCompiler.Compile(request with { Questions = questions }).Issues);
        Assert.NotEmpty(WhatsAppFlowCompiler.Compile(request with { Format = 2 }).Issues);
        questions[0] = questions[0] with { Type = "group" };
        Assert.NotEmpty(WhatsAppFlowCompiler.Compile(request with { Questions = questions }).Issues);
        Assert.NotEmpty(WhatsAppFlowCompiler.Compile(request with { SubmitLabel = new Dictionary<string, string>() }).Issues);
    }

    [Fact]
    public void Missing_secondary_texts_are_blocked_without_using_the_original_language()
    {
        var request = Request() with { OriginalCulture = "en", SubmitLabel = new Dictionary<string, string> { ["en"] = "Finish" },
            Questions = [Question(1, "shortText") with { Label = new Dictionary<string, string> { ["en"] = "Original" } }] };
        var result = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(result.Flows);
        Assert.Contains(result.Issues, issue => issue.Path == "draft.fields[0].label.es" && issue.Culture == "es");
        Assert.Contains(result.Issues, issue => issue.Path == "draft.submitLabel.es" && issue.Culture == "es");
    }

    [Fact]
    public void Optional_false_is_a_value_and_missing_is_not_false()
    {
        var mapping = WhatsAppFlowCompiler.Compile(Request()).Flows[0].Mapping.Single(x => x.Type == "boolean");
        var no = WhatsAppFlowValueCodec.Decode(mapping with { Required = false }, "false");
        Assert.True(no.IsSuccess); Assert.True(no.Value.IsPresent); Assert.False(no.Value.Boolean);
        var absent = WhatsAppFlowValueCodec.Decode(mapping with { Required = false }, null);
        Assert.True(absent.IsSuccess); Assert.False(absent.Value.IsPresent); Assert.Null(absent.Value.Boolean);
        Assert.True(WhatsAppFlowValueCodec.Decode(mapping, null).IsFailure);
        Assert.True(WhatsAppFlowValueCodec.Decode(mapping, "0").IsFailure);
    }

    [Theory]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
    [InlineData("0,0000000000000000000000000001", "0.0000000000000000000000000001")]
    [InlineData("-0.00", "0")]
    [InlineData("1.2300", "1.23")]
    public void Decimal_strings_round_trip_exactly_in_the_server_codec(string input, string expected)
    {
        var mapping = WhatsAppFlowCompiler.Compile(Request()).Flows[0].Mapping.Single(x => x.Type == "decimal");
        var result = WhatsAppFlowValueCodec.Decode(mapping, input);
        Assert.True(result.IsSuccess); Assert.Equal(expected, result.Value.Decimal);
    }

    [Theory]
    [InlineData("79228162514264337593543950336")]
    [InlineData("0.00000000000000000000000000001")]
    [InlineData("1e2")]
    [InlineData("1,000.00")]
    [InlineData("1\n")]
    public void Decimal_values_that_would_overflow_round_or_change_grammar_are_rejected(string input)
    {
        var mapping = WhatsAppFlowCompiler.Compile(Request()).Flows[0].Mapping.Single(x => x.Type == "decimal");
        Assert.True(WhatsAppFlowValueCodec.Decode(mapping, input).IsFailure);
    }

    [Fact]
    public void Dates_and_choices_are_validated_against_the_exact_mapping()
    {
        var mapping = WhatsAppFlowCompiler.Compile(Request()).Flows[0].Mapping;
        var date = mapping.Single(x => x.Type == "date");
        Assert.Equal(new DateOnly(2024, 2, 29), WhatsAppFlowValueCodec.Decode(date, "2024-02-29").Value.Date);
        Assert.True(WhatsAppFlowValueCodec.Decode(date, "2025-02-29").IsFailure);
        Assert.True(WhatsAppFlowValueCodec.Decode(date, "2024-02-29T00:00:00Z").IsFailure);
        var choice = mapping.Single(x => x.Type == "choice");
        Assert.Equal(ChoiceId, WhatsAppFlowValueCodec.Decode(choice, ChoiceId.ToString("D")).Value.ChoiceId);
        Assert.True(WhatsAppFlowValueCodec.Decode(choice, Guid.NewGuid().ToString("D")).IsFailure);
    }

    [Fact]
    public void Forty_questions_and_fifty_choices_keep_one_screen_and_larger_shapes_are_blocked()
    {
        var questions = Enumerable.Range(1, 40).Select(value => Question(value, "shortText") with { MaxLength = 50 }).ToArray();
        var compilation = WhatsAppFlowCompiler.Compile(Request() with { Questions = questions });
        Assert.Empty(compilation.Issues); Assert.All(compilation.Flows, flow => Assert.Equal(42, flow.ComponentCount));
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { Questions = questions.Append(Question(41, "shortText")).ToArray() }).Flows);
        var choice = Question(1, "choice") with { Options = Enumerable.Range(100, 50).Select(value =>
            new StructuredFormChoice(new Guid($"019a0000-0000-7000-8000-{value:000000000000}"), Text("Opción " + value))).ToArray() };
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [choice] }).Issues);
        Assert.NotEmpty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [choice with { Options = choice.Options.Append(new(ChoiceId, Text("Extra"))).ToArray() }] }).Issues);
    }

    [Fact]
    public void Worst_case_escaped_response_budget_blocks_publication_with_localized_field_issues()
    {
        var questions = Enumerable.Range(1, 40).Select(value => Question(value, "longText") with { MaxLength = 10000 }).ToArray();

        var result = WhatsAppFlowCompiler.Compile(Request() with { Questions = questions });

        Assert.Empty(result.Flows);
        Assert.Contains(result.Issues, issue => issue.Error.Code == "WhatsApp.Flow.ResponseTooLarge"
            && issue.FieldId is not null && issue.Path.EndsWith(".maxLength", StringComparison.Ordinal) && issue.Culture == "en");
        Assert.Contains(result.Issues, issue => issue.Error.Code == "WhatsApp.Flow.ResponseTooLarge"
            && issue.FieldId is not null && issue.Path.EndsWith(".maxLength", StringComparison.Ordinal) && issue.Culture == "es");
        Assert.All(result.Issues.Where(issue => issue.Error.Code == "WhatsApp.Flow.ResponseTooLarge"), issue =>
        {
            Assert.Contains(questions, question => question.FieldId == issue.FieldId);
            Assert.Contains("maxLength", issue.Path, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Non_text_answers_that_cross_the_budget_still_point_to_reducible_text_fields()
    {
        var questions = new[] { Question(1, "shortText") with { MaxLength = 2000 } }
            .Concat(Enumerable.Range(2, 39).Select(value => Question(value, "decimal"))).ToArray();

        var result = WhatsAppFlowCompiler.Compile(Request() with { Questions = questions });

        Assert.Empty(result.Flows);
        Assert.Contains(result.Issues, issue => issue.Error.Code == "WhatsApp.Flow.ResponseTooLarge"
            && issue.FieldId == questions[0].FieldId && issue.Path == "draft.fields[0].maxLength" && issue.Culture == "en");
        Assert.Contains(result.Issues, issue => issue.Error.Code == "WhatsApp.Flow.ResponseTooLarge"
            && issue.FieldId == questions[0].FieldId && issue.Path == "draft.fields[0].maxLength" && issue.Culture == "es");
    }
    [Fact]
    public void Conservative_utf16_budget_preserves_emoji_and_blocks_without_cutting_surrogates()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 10));
        var question = Question(1, "shortText") with { Label = Text(text) };
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [question] }).Issues);
        var blocked = question with { Label = Text(text + "x") };
        Assert.NotEmpty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [blocked] }).Issues);
        Assert.Equal(text + "x", blocked.Label["es"]);
    }
    [Fact]
    public void Duplicate_or_noncanonical_identity_is_blocked_before_wire_generation()
    {
        var question = Question(1, "shortText");
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [question, question] }).Flows);
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [question with { Key = "label_key" }] }).Flows);
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [question with { FieldId = Guid.Empty }] }).Flows);
    }
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Synthetic_review_fixture_matches_the_exact_compiled_bytes(string culture)
    {
        using var stream = typeof(WhatsAppFlowCompilerTests).Assembly.GetManifestResourceStream($"FlowProfile.{WhatsAppFlowCompiler.ProfileRevision}-{culture}.json")
            ?? throw new InvalidOperationException("The synthetic flow fixture is missing.");
        using var reader = new StreamReader(stream);
        var request = Request();
        var catalog = Question(7, "choice") with { Options = null, SourceCatalogId = Guid.Parse("019a0000-0000-7000-8000-000000000200") };
        var compiled = WhatsAppFlowCompiler.Compile(request with { Questions = [.. request.Questions, catalog] }).Flows.Single(flow => flow.Culture == culture);
        FlowProfileFixtureWriter.Export(WhatsAppFlowCompiler.ProfileRevision, culture, compiled.Json);
        Assert.Equal(reader.ReadToEnd(), compiled.Json);
    }
    [Fact]
    public void Name_and_present_help_require_each_target_language_and_titles_use_that_language()
    {
        var request = Request();
        var compilation = WhatsAppFlowCompiler.Compile(request);
        foreach (var flow in compilation.Flows)
        {
            using var json = JsonDocument.Parse(flow.Json);
            Assert.Equal(request.Name[flow.Culture], json.RootElement.GetProperty("screens")[0].GetProperty("title").GetString());
        }
        Assert.Contains(WhatsAppFlowCompiler.Compile(request with { Name = new Dictionary<string, string> { ["es"] = "Original" } }).Issues,
            issue => issue.Path == "name.en" && issue.Culture == "en");
        var question = request.Questions[0] with { Help = new Dictionary<string, string> { ["es"] = "Ayuda" } };
        var incomplete = WhatsAppFlowCompiler.Compile(request with { Questions = [question] });
        Assert.Empty(incomplete.Flows);
        Assert.Contains(incomplete.Issues, issue => issue.Path == "draft.fields[0].help.en" && issue.FieldId == question.FieldId);
    }
    private static readonly Guid ChoiceId = new("019a0000-0000-7000-8000-000000000100");
    private static Dictionary<string, string> Text(string text) => new Dictionary<string, string> { ["es"] = text, ["en"] = text switch { "Pregunta" => "Question", "Opción" => "Option", _ => text } };
    private static StructuredFormQuestion Question(int value, string type)
    {
        var id = new Guid($"019a0000-0000-7000-8000-{value:000000000000}");
        return new(id, "field_" + id.ToString("N"), type, Text("Pregunta"), null, true,
            type == "shortText" ? 80 : type == "longText" ? 600 : null,
            type == "choice" ? [new(ChoiceId, Text("Opción"))] : null);
    }
    private static StructuredFormCheckRequest Request() => new(1, "es", new Dictionary<string, string> { ["es"] = "Formulario", ["en"] = "Form" }, new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" },
        new[] { "shortText", "longText", "decimal", "date", "boolean", "choice" }.Select((type, index) => Question(index + 1, type)).ToArray(), ["es", "en"]);
}
