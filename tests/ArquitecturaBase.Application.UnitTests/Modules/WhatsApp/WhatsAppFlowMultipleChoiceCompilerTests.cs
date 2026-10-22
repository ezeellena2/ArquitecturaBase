using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowMultipleChoiceCompilerTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void New_compilations_use_revision_nine_without_changing_the_wire_or_codec_version()
    {
        var result = WhatsAppFlowCompiler.Compile(Request([Multiple(1)]));

        Assert.Empty(result.Issues);
        Assert.Equal("flow-static-7.3-r9", WhatsAppFlowCompiler.ProfileRevision);
        Assert.Equal(1, WhatsAppFlowCompiler.CodecVersion);
        foreach (var flow in result.Flows)
        {
            using var document = JsonDocument.Parse(flow.Json);
            Assert.Equal("7.3", document.RootElement.GetProperty("version").GetString());
            Assert.False(document.RootElement.TryGetProperty("data_api_version", out _));
        }
    }

    [Theory]
    [InlineData("es", false)]
    [InlineData("en", false)]
    [InlineData("es", true)]
    [InlineData("en", true)]
    public void Own_and_dynamic_choices_compile_as_localized_checkbox_groups(string culture, bool dynamic)
    {
        var question = Multiple(1, dynamic);
        var result = WhatsAppFlowCompiler.Compile(Request([question]) with { TargetCultures = [culture] });

        Assert.Empty(result.Issues);
        var flow = Assert.Single(result.Flows);
        Assert.Equal(culture, flow.Culture);
        using var document = JsonDocument.Parse(flow.Json);
        var screen = document.RootElement.GetProperty("screens")[0];
        var controls = Controls(screen);
        var control = controls[0];
        Assert.Equal("CheckboxGroup", control.GetProperty("type").GetString());
        Assert.Equal(question.Key, control.GetProperty("name").GetString());
        Assert.Equal(question.Label[culture], control.GetProperty("label").GetString());
        Assert.True(control.GetProperty("required").GetBoolean());
        Assert.Equal(1, control.GetProperty("min-selected-items").GetInt32());
        Assert.Equal(3, control.GetProperty("max-selected-items").GetInt32());
        Assert.Equal(culture == "es" ? "Elegí hasta 3" : "Choose up to 3",
            control.GetProperty("description").GetString());
        Assert.False(control.TryGetProperty("init-value", out _));
        Assert.Equal("complete", controls[^1].GetProperty("on-click-action").GetProperty("name").GetString());
        Assert.Equal(Ref("form." + question.Key),
            controls[^1].GetProperty("on-click-action").GetProperty("payload").GetProperty(question.Key).GetString());

        var mapping = Assert.Single(flow.Mapping);
        Assert.Equal(question.Type, mapping.Type);
        Assert.Equal(question.FieldId, mapping.FieldId);
        var sealedMapping = JsonSerializer.SerializeToElement(mapping, Json);
        Assert.Equal(1, sealedMapping.GetProperty("selection").GetProperty("minSelected").GetInt32());
        Assert.Equal(3, sealedMapping.GetProperty("selection").GetProperty("maxSelected").GetInt32());

        if (dynamic)
        {
            Assert.Equal(Ref("data." + question.Key + "_options"), control.GetProperty("data-source").GetString());
            var options = screen.GetProperty("data").GetProperty(question.Key + "_options");
            Assert.Equal("array", options.GetProperty("type").GetString());
            var properties = options.GetProperty("items").GetProperty("properties");
            foreach (var name in new[] { "id", "title", "description" })
                Assert.Equal("string", properties.GetProperty(name).GetProperty("type").GetString());
            Assert.Empty(options.GetProperty("__example__").EnumerateArray());
            Assert.Empty(mapping.Choices);
        }
        else
        {
            var options = control.GetProperty("data-source").EnumerateArray().ToArray();
            Assert.Equal(question.Options!.Select(option => option.ChoiceId.ToString("D")),
                options.Select(option => option.GetProperty("id").GetString()));
            Assert.Equal(question.Options!.Select(option => option.Label[culture]),
                options.Select(option => option.GetProperty("title").GetString()));
            Assert.Equal(question.Options!.Select(option => option.ChoiceId), mapping.Choices);
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void Authored_help_replaces_the_localized_default_without_clipping(string culture)
    {
        var help = new string('h', 300);
        var question = Multiple(1) with { Label = Both(new string('l', 30)), Help = Both(help) };
        var result = WhatsAppFlowCompiler.Compile(Request([question]) with { TargetCultures = [culture] });

        Assert.Empty(result.Issues);
        using var document = JsonDocument.Parse(Assert.Single(result.Flows).Json);
        var control = Controls(document.RootElement.GetProperty("screens")[0])[0];
        Assert.Equal(new string('l', 30), control.GetProperty("label").GetString());
        Assert.Equal(help, control.GetProperty("description").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Labels_over_thirty_or_help_over_three_hundred_are_identity_errors(bool label)
    {
        var question = Multiple(1) with
        {
            Label = Both(new string('l', label ? 31 : 30)),
            Help = Both(new string('h', label ? 300 : 301)),
        };

        var result = WhatsAppFlowCompiler.Compile(Request([question]));

        Assert.Empty(result.Flows);
        foreach (var culture in new[] { "es", "en" })
            Assert.Contains(result.Issues, issue => issue.FieldId == question.FieldId
                && issue.Path == "draft.fields[0]." + (label ? "label." : "help.") + culture
                && issue.Culture == culture && issue.Limit == (label ? 30 : 300));
        Assert.Equal(label ? 31 : 30, question.Label["es"].Length);
        Assert.Equal(label ? 300 : 301, question.Help!["en"].Length);
    }

    [Fact]
    public void A_present_help_requires_the_requested_translation_without_falling_back()
    {
        var question = Multiple(1) with { Help = new Dictionary<string, string> { ["es"] = "Ayuda original" } };

        var result = WhatsAppFlowCompiler.Compile(Request([question]));

        Assert.Empty(result.Flows);
        Assert.Contains(result.Issues, issue => issue.FieldId == question.FieldId
            && issue.Path == "draft.fields[0].help.en" && issue.Culture == "en");
    }

    [Theory]
    [InlineData(-1, 3, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 21, false)]
    [InlineData(3, 2, true)]
    [InlineData(0, 3, true)]
    [InlineData(1, 3, false)]
    [InlineData(21, 20, true)]
    [InlineData(4, 5, true)]
    public void Impossible_ranges_or_required_disagreement_never_emit_a_flow(int minimum, int maximum, bool required)
    {
        var question = Multiple(1) with { Selection = new(minimum, maximum), Required = required };

        var result = WhatsAppFlowCompiler.Compile(Request([question]));

        Assert.Empty(result.Flows);
        Assert.NotEmpty(result.Issues);
        Assert.Contains(result.Issues, issue => issue.FieldId == question.FieldId);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Maximum_can_exceed_the_own_option_count_as_an_upper_bound(int minimum, bool required)
    {
        var question = Multiple(1) with { Selection = new(minimum, 20), Required = required };

        var result = WhatsAppFlowCompiler.Compile(Request([question]));

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Flows.Count);
    }

    [Fact]
    public void A_dynamic_minimum_is_not_rejected_using_an_unavailable_recipient_option_count()
    {
        var question = Multiple(1, dynamic: true) with { Selection = new(20, 20) };

        var result = WhatsAppFlowCompiler.Compile(Request([question]));

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Flows.Count);
    }

    [Fact]
    public void Selection_is_required_only_for_multiple_choice()
    {
        var missing = Multiple(1) with { Selection = null };
        var scalar = Scalar(1) with { Selection = new(0, 1) };

        foreach (var question in new[] { missing, scalar })
        {
            var result = WhatsAppFlowCompiler.Compile(Request([question]));
            Assert.Empty(result.Flows);
            Assert.Contains(result.Issues, issue => issue.FieldId == question.FieldId);
        }
    }

    [Fact]
    public void Twenty_own_options_fit_and_a_twenty_first_is_not_silently_trimmed()
    {
        var question = Multiple(1) with { Options = Choices(1, 20), Selection = new(1, 20) };
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request([question])).Issues);
        var tooMany = question with { Options = Choices(1, 21) };

        var result = WhatsAppFlowCompiler.Compile(Request([tooMany]));

        Assert.Empty(result.Flows);
        Assert.Contains(result.Issues, issue => issue.FieldId == question.FieldId);
        Assert.Equal(21, tooMany.Options!.Count);
    }

    [Fact]
    public void Initial_value_schemas_contain_scalars_but_never_multiple_choice_keys()
    {
        var scalar = Scalar(1);
        var own = Multiple(2);
        var dynamic = Multiple(3, dynamic: true);
        var result = WhatsAppFlowCompiler.Compile(Request([scalar, own, dynamic]));

        Assert.Empty(result.Issues);
        foreach (var flow in result.Flows)
        {
            using var document = JsonDocument.Parse(flow.Json);
            var screen = document.RootElement.GetProperty("screens")[0];
            var form = screen.GetProperty("layout").GetProperty("children")[0];
            Assert.Equal(Ref("data.prefill_FORM"), form.GetProperty("init-values").GetString());
            var initial = screen.GetProperty("data").GetProperty("prefill_FORM").GetProperty("properties");
            Assert.Equal(scalar.Key, Assert.Single(initial.EnumerateObject()).Name);
            Assert.False(initial.TryGetProperty(own.Key, out _));
            Assert.False(initial.TryGetProperty(dynamic.Key, out _));
            Assert.True(screen.GetProperty("data").TryGetProperty(dynamic.Key + "_options", out _));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_multiple_choice_only_screen_has_no_initial_value_map(bool dynamic)
    {
        var result = WhatsAppFlowCompiler.Compile(Request([Multiple(1, dynamic)]));

        Assert.Empty(result.Issues);
        foreach (var flow in result.Flows)
        {
            using var document = JsonDocument.Parse(flow.Json);
            var screen = document.RootElement.GetProperty("screens")[0];
            Assert.False(screen.GetProperty("layout").GetProperty("children")[0].TryGetProperty("init-values", out _));
            if (screen.TryGetProperty("data", out var data))
                Assert.DoesNotContain(data.EnumerateObject(), property => property.Name.StartsWith("prefill_", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prefill_keeps_scalar_values_and_rejects_every_multiple_choice_entry(bool dynamic)
    {
        var scalar = Scalar(1);
        var multiple = Multiple(2, dynamic) with { Required = false, Selection = new(0, 3) };
        var compilation = WhatsAppFlowCompiler.Compile(Request([scalar, multiple]));
        Assert.Empty(compilation.Issues);
        var projection = Projection(compilation.Flows[0]);
        var sealedField = JsonSerializer.SerializeToElement(projection.Mapping.Single(field => field.FieldId == multiple.FieldId), Json);
        Assert.Equal(0, sealedField.GetProperty("selection").GetProperty("minSelected").GetInt32());
        Assert.Equal(3, sealedField.GetProperty("selection").GetProperty("maxSelected").GetInt32());

        var scalarOnly = WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string> { [scalar.Key] = "Visit" });
        Assert.True(scalarOnly.IsSuccess);
        var initial = Assert.Single(scalarOnly.Value).Value;
        Assert.Equal("Visit", Assert.Single(initial).Value);
        Assert.False(initial.ContainsKey(multiple.Key));
        var empty = WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string>());
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value);

        foreach (var forbidden in new[] { "[]", ChoiceId(2, 1).ToString("D"), "[\"" + ChoiceId(2, 1).ToString("D") + "\"]" })
        {
            var rejected = WhatsAppFlowPrefillPolicy.Build(projection,
                new Dictionary<string, string> { [scalar.Key] = "Visit", [multiple.Key] = forbidden });
            Assert.True(rejected.IsFailure);
            Assert.Equal("WhatsApp.Flow.InvalidValue", rejected.Error.Code);
        }
    }

    [Fact]
    public void Forty_maximum_twenty_arrays_are_blocked_at_the_reducible_selection_limit()
    {
        var questions = Enumerable.Range(1, 40).Select(index => Multiple(index) with
        {
            Required = false, Selection = new(0, 20), Options = Choices(index, 20),
        }).ToArray();

        var result = WhatsAppFlowCompiler.Compile(Request(questions));

        Assert.Empty(result.Flows);
        foreach (var culture in new[] { "es", "en" })
        {
            var issues = result.Issues.Where(issue => issue.Error.Code == "WhatsApp.Flow.ResponseTooLarge"
                && issue.Culture == culture).ToArray();
            Assert.NotEmpty(issues);
            Assert.All(issues, issue =>
            {
                var index = Array.FindIndex(questions, question => question.FieldId == issue.FieldId);
                Assert.InRange(index, 0, questions.Length - 1);
                Assert.Equal("draft.fields[" + index + "].selection.maxSelected", issue.Path);
            });
        }
    }

    [Fact]
    public void Forty_maximum_one_arrays_fit_without_relaxing_the_response_budget()
    {
        var questions = Enumerable.Range(1, 40).Select(index => Multiple(index) with
        {
            Required = false, Selection = new(0, 1), Options = Choices(index, 20),
        }).ToArray();

        var result = WhatsAppFlowCompiler.Compile(Request(questions));

        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Flows.Count);
        Assert.All(result.Flows, flow => Assert.Equal(42, flow.ComponentCount));
    }

    [Fact]
    public void Changing_only_the_minimum_does_not_replay_an_accepted_publication()
    {
        var request = PublicationRequest(Request([Multiple(1)]));
        var manifest = Manifest(WhatsAppFlowPublicationPolicy.RequestFingerprint(request));
        var changed = request with
        {
            Snapshot = request.Snapshot with
            {
                Questions = [request.Snapshot.Questions[0] with { Selection = new(2, 3) }],
            },
        };

        Assert.True(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, request));
        Assert.Same(request.Cultures, changed.Cultures);
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, changed));
    }

    [Fact]
    public void Changing_only_the_maximum_does_not_replay_an_accepted_publication()
    {
        var request = PublicationRequest(Request([Multiple(1)]));
        var manifest = Manifest(WhatsAppFlowPublicationPolicy.RequestFingerprint(request));
        var changed = request with
        {
            Snapshot = request.Snapshot with
            {
                Questions = [request.Snapshot.Questions[0] with { Selection = new(1, 2) }],
            },
        };

        Assert.True(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, request));
        Assert.Same(request.Cultures, changed.Cultures);
        Assert.False(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, changed));
    }

    [Fact]
    public void A_null_selection_preserves_the_request_fingerprint_from_before_revision_nine()
    {
        var scalar = Scalar(1) with { Label = Both("Field"), Required = false };
        var snapshot = Request([scalar]) with { Name = Both("Form"), SubmitLabel = Both("Send") };
        var request = PublicationRequest(snapshot) with { ProfileRevision = "flow-static-7.3-r8" };
        var baselineHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(LegacyFingerprintJson)));

        Assert.Null(scalar.Selection);
        Assert.DoesNotContain("\"selection\"", JsonSerializer.Serialize(request, Json), StringComparison.Ordinal);
        Assert.Equal(baselineHash, WhatsAppFlowPublicationPolicy.RequestFingerprint(request));
        Assert.True(WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(Manifest(baselineHash), request));
    }

    // Fixed pre-C3 format-2 fingerprint payload. This is historical test data, not a
    // second canonicalizer; in particular it must not gain Selection, Source or Files.
    private const string LegacyFingerprintJson =
        """{"fingerprintFormat":2,"operationId":"019a0000-0000-7000-8000-000000000900","definitionId":"019a0000-0000-7000-8000-000000000901","versionId":"019a0000-0000-7000-8000-000000000902","actorUserId":"019a0000-0000-7000-8000-000000000903","requiredPermissions":["permission"],"publicationFingerprint":"publication","profileRevision":"flow-static-7.3-r8","cultures":[{"culture":"en","compilationFingerprint":"en-hash"},{"culture":"es","compilationFingerprint":"es-hash"}],"snapshot":{"format":1,"originalCulture":"es","name":[{"key":"en","value":"Form"},{"key":"es","value":"Form"}],"submitLabel":[{"key":"en","value":"Send"},{"key":"es","value":"Send"}],"questions":[{"fieldId":"019a0000-0000-7000-8000-000000000001","key":"field_019a0000000070008000000000000001","type":"shortText","label":[{"key":"en","value":"Field"},{"key":"es","value":"Field"}],"help":null,"required":false,"maxLength":80,"options":null}],"targetCultures":["en","es"]}}""";

    private static WhatsAppFlowProjection Projection(WhatsAppCompiledFlowResponse flow) =>
        new(1, flow.Json, flow.Hash, WhatsAppFlowCompiler.ProfileRevision, WhatsAppFlowCompiler.CodecVersion, flow.Culture,
            flow.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type,
                field.Required, field.MaxLength, field.Choices, field.Files, field.Selection)).ToArray());

    private static WhatsAppFlowStoredManifest Manifest(string fingerprint) =>
        new(2, fingerprint, "publication", new("waba", "phone", "v25.0", "configuration"), ["permission"]);

    private static StructuredFormPublicationRequest PublicationRequest(StructuredFormCheckRequest snapshot) =>
        new(Id(900), Id(901), Id(902), Id(903), ["permission"], "publication", "flow-static-7.3-r9",
            [new("es", "es-hash"), new("en", "en-hash")], snapshot);

    private static StructuredFormCheckRequest Request(IReadOnlyList<StructuredFormQuestion> questions) =>
        new(1, "es", Text("Visita", "Visit"), Text("Enviar", "Send"), questions, ["es", "en"]);

    private static StructuredFormQuestion Multiple(int index, bool dynamic = false) =>
        new(Id(index), Key(index), "multiChoice", Text("Elegí tareas", "Choose tasks"), null, true, null,
            dynamic ? null : Choices(index, 3), dynamic ? Id(800) : null, Selection: new(1, 3));

    private static StructuredFormQuestion Scalar(int index) =>
        new(Id(index), Key(index), "shortText", Both("Field"), null, false, 80, null);

    private static StructuredFormChoice[] Choices(int parent, int count) =>
        Enumerable.Range(1, count).Select(index => new StructuredFormChoice(ChoiceId(parent, index),
            Text("Opción " + index, "Option " + index))).ToArray();

    private static Guid Id(int index) => new($"019a0000-0000-7000-8000-{index:000000000000}");
    private static Guid ChoiceId(int parent, int index) => Id(10000 + parent * 100 + index);
    private static string Key(int index) => "field_" + Id(index).ToString("N");
    private static Dictionary<string, string> Both(string value) => Text(value, value);
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };
    private static string Ref(string path) => "$" + "{" + path + "}";
    private static JsonElement[] Controls(JsonElement screen) =>
        screen.GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().ToArray();
}
