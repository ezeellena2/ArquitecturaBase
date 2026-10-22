using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowPrefillTests
{
    [Fact]
    public void New_publications_declare_a_dynamic_initial_value_map_for_the_six_scalars()
    {
        var request = Request();
        var result = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(result.Issues);
        Assert.Equal("flow-static-7.3-r8", WhatsAppFlowCompiler.ProfileRevision);
        Assert.Equal(1, WhatsAppFlowCompiler.CodecVersion);
        using var json = JsonDocument.Parse(result.Flows[0].Json);
        var screen = json.RootElement.GetProperty("screens")[0];
        var form = screen.GetProperty("layout").GetProperty("children")[0];
        Assert.Equal(Ref("data.prefill_FORM"), form.GetProperty("init-values").GetString());
        var properties = screen.GetProperty("data").GetProperty("prefill_FORM").GetProperty("properties");
        Assert.Equal(request.Questions.Select(question => question.Key), properties.EnumerateObject().Select(property => property.Name));
        Assert.All(properties.EnumerateObject(), property => Assert.Equal("string", property.Value.GetProperty("type").GetString()));
    }

    [Fact]
    public void Scalar_maps_follow_the_screen_that_owns_the_control_and_never_include_files()
    {
        var scalar = Request().Questions;
        var request = Request() with { Questions = [scalar[0], Photo(), scalar[4], Document(), scalar[2]] };
        var result = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(result.Issues);
        using var json = JsonDocument.Parse(result.Flows[0].Json);
        var screens = json.RootElement.GetProperty("screens");
        Assert.Equal(3, screens.GetArrayLength());
        var data = screens[0].GetProperty("data");
        for (var index = 0; index < 3; index++)
        {
            var id = index == 0 ? "FORM" : "FORM_" + (char)('A' + index - 1);
            var form = screens[index].GetProperty("layout").GetProperty("children")[0];
            Assert.Equal(Ref(index == 0 ? "data.prefill_" + id : "screen.FORM.data.prefill_" + id), form.GetProperty("init-values").GetString());
            var keys = data.GetProperty("prefill_" + id).GetProperty("properties").EnumerateObject().Select(property => property.Name).ToArray();
            Assert.Single(keys);
            Assert.Equal(request.Questions[index == 0 ? 0 : index == 1 ? 2 : 4].Key, keys[0]);
        }
        Assert.DoesNotContain(request.Questions.Where(question => question.Files is not null), question => data.GetRawText().Contains(question.Key, StringComparison.Ordinal));
    }

    [Fact]
    public void A_first_file_only_screen_still_exposes_maps_used_by_later_scalar_screens()
    {
        var request = Request() with { Questions = [Photo(), Document(), Request().Questions[0]] };
        var compiled = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(compiled.Issues);
        using var json = JsonDocument.Parse(compiled.Flows[0].Json);
        var first = json.RootElement.GetProperty("screens")[0];
        Assert.True(first.TryGetProperty("data", out var data));
        Assert.True(data.TryGetProperty("prefill_FORM_A", out _));
        Assert.False(first.GetProperty("layout").GetProperty("children")[0].TryGetProperty("init-values", out _));
    }

    [Fact]
    public void Empty_prefill_is_distinct_from_zero_or_false_and_does_not_supply_missing_required_values()
    {
        var request = Request(); var projection = Projection(request);
        var values = new Dictionary<string, string> { [request.Questions[2].Key] = "0", [request.Questions[4].Key] = "false" };
        var result = WhatsAppFlowPrefillPolicy.Build(projection, values);
        Assert.True(result.IsSuccess);
        var initial = Assert.Single(result.Value).Value;
        Assert.Equal(2, initial.Count);
        Assert.Equal("0", initial[request.Questions[2].Key]);
        Assert.Equal("false", initial[request.Questions[4].Key]);
        Assert.False(initial.ContainsKey(request.Questions[0].Key));
        values.Clear();
        Assert.Equal(2, initial.Count);
        var empty = WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string>());
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value);
    }

    [Fact]
    public void Data_maps_are_split_without_moving_photo_or_document_answers()
    {
        var scalar = Request().Questions;
        var request = Request() with { Questions = [scalar[0], Photo(), scalar[4], Document(), scalar[2]] };
        var values = new Dictionary<string, string> { [scalar[0].Key] = "Lugar", [scalar[4].Key] = "false" };
        var result = WhatsAppFlowPrefillPolicy.Build(Projection(request), values);
        Assert.True(result.IsSuccess);
        Assert.Equal("Lugar", Assert.Single(result.Value["prefill_FORM"]).Value);
        Assert.Equal("false", Assert.Single(result.Value["prefill_FORM_A"]).Value);
        Assert.False(result.Value.ContainsKey("prefill_FORM_B"));
        Assert.True(WhatsAppFlowPrefillPolicy.Build(Projection(request), new Dictionary<string, string> { [request.Questions[1].Key] = "media" }).IsFailure);
        Assert.True(WhatsAppFlowPrefillPolicy.Build(Projection(request), new Dictionary<string, string> { ["unknown"] = "value" }).IsFailure);
    }

    [Theory]
    [InlineData("flow-static-7.3-r8")]
    [InlineData("flow-static-7.3-r9")]
    public void Compatible_published_profiles_accept_prefill_without_activating_revision_nine(string profile)
    {
        var request = Request();
        var compiled = Projection(request);
        var projection = compiled with { ProfileRevision = profile };
        var values = new Dictionary<string, string>
        {
            [request.Questions[0].Key] = "Stored place",
            [request.Questions[2].Key] = "0",
            [request.Questions[3].Key] = "2026-10-21",
            [request.Questions[4].Key] = "false",
            [request.Questions[5].Key] = request.Questions[5].Options![0].ChoiceId.ToString("D"),
        };

        var result = WhatsAppFlowPrefillPolicy.Build(projection, values);

        Assert.True(result.IsSuccess);
        var initial = Assert.Single(result.Value).Value;
        Assert.Equal(values.Count, initial.Count);
        foreach (var value in values) Assert.Equal(value.Value, initial[value.Key]);
        Assert.Equal("flow-static-7.3-r8", WhatsAppFlowCompiler.ProfileRevision);
        Assert.Equal("flow-static-7.3-r8", compiled.ProfileRevision);
        Assert.Equal(compiled.Json, projection.Json);
        Assert.Equal(compiled.Hash, projection.Hash);
    }

    [Fact]
    public void Old_published_profiles_keep_empty_data_and_never_accept_a_new_prefill()
    {
        var request = Request(); var current = Projection(request);
        var old = current with { ProfileRevision = "flow-static-7.3-r7", Json = "old immutable bytes", Hash = "old immutable hash" };
        var result = WhatsAppFlowPrefillPolicy.Build(old, new Dictionary<string, string>());
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.True(WhatsAppFlowPrefillPolicy.Build(old, new Dictionary<string, string> { [request.Questions[0].Key] = "value" }).IsFailure);
        Assert.Equal("old immutable bytes", old.Json);
        Assert.Equal("old immutable hash", old.Hash);
    }

    private static WhatsAppFlowProjection Projection(StructuredFormCheckRequest request)
    {
        var result = WhatsAppFlowCompiler.Compile(request).Flows[0];
        return new(1, result.Json, result.Hash, WhatsAppFlowCompiler.ProfileRevision, 1, result.Culture,
            result.Mapping.Select(field => new WhatsAppFlowProjectionField(field.FieldId, field.Key, field.Type, field.Required, field.MaxLength, field.Choices, field.Files)).ToArray());
    }
    private static StructuredFormCheckRequest Request() => new(1, "es", Text("Visita"), Text("Enviar"), [
        Question("shortText") with { MaxLength = 80 }, Question("longText") with { MaxLength = 600 }, Question("decimal"), Question("date"), Question("boolean"),
        Question("choice") with { Options = [new(Guid.CreateVersion7(), Text("Opción"))] }], ["es", "en"]);
    private static StructuredFormQuestion Question(string type)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text("Campo"), null, true, null, null);
    }
    private static StructuredFormQuestion Photo() => Question("photo") with { Files = new(1, 1, "camera") };
    private static StructuredFormQuestion Document() => Question("document") with { Files = new(1, 1) };
    private static Dictionary<string, string> Text(string text) => new() { ["es"] = text, ["en"] = text };
    private static string Ref(string path) => "$" + "{" + path + "}";
}
