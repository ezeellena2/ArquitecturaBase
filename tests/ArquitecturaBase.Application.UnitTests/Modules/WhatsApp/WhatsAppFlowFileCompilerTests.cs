using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppFlowFileCompilerTests
{
    [Fact]
    public void Files_start_screens_and_are_only_submitted_as_top_level_complete_values()
    {
        var request = Request();
        var result = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(result.Issues);
        using var document = JsonDocument.Parse(result.Flows[0].Json);
        var screens = document.RootElement.GetProperty("screens").EnumerateArray().ToArray();
        Assert.Equal(3, screens.Length);
        Assert.Equal("FORM", screens[0].GetProperty("id").GetString());
        var photo = Controls(screens[1])[0];
        Assert.Equal("PhotoPicker", photo.GetProperty("type").GetString());
        Assert.Equal("camera", photo.GetProperty("photo-source").GetString());
        Assert.Equal(1, photo.GetProperty("min-uploaded-photos").GetInt32());
        Assert.Equal(4, photo.GetProperty("max-uploaded-photos").GetInt32());
        Assert.False(photo.TryGetProperty("required", out _));
        var pdf = Controls(screens[2])[0];
        Assert.Equal("DocumentPicker", pdf.GetProperty("type").GetString());
        Assert.Equal(0, pdf.GetProperty("min-uploaded-documents").GetInt32());
        Assert.DoesNotContain("text/csv", pdf.GetProperty("allowed-mime-types").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(3, Controls(screens[2]).Length);
        for (var i = 0; i < screens.Length - 1; i++)
        {
            var action = Controls(screens[i])[^1].GetProperty("on-click-action");
            Assert.Equal("navigate", action.GetProperty("name").GetString());
            Assert.Empty(action.GetProperty("payload").EnumerateObject());
        }
        var payload = Controls(screens[^1])[^1].GetProperty("on-click-action").GetProperty("payload");
        Assert.Equal("${screen.FORM_2.form." + request.Questions[1].Key + "}", payload.GetProperty(request.Questions[1].Key).GetString());
        Assert.Equal("${form." + request.Questions[2].Key + "}", payload.GetProperty(request.Questions[2].Key).GetString());
        Assert.Equal(4, payload.EnumerateObject().Count());
        Assert.DoesNotContain("init-values", result.Flows[0].Json);
        Assert.Equal(request.Questions[1].Files, result.Flows[0].Mapping[1].Files);
    }

    [Fact]
    public void File_budget_is_rejected_without_emitting_partial_flows()
    {
        var request = Request();
        var result = WhatsAppFlowCompiler.Compile(request with { Questions = [request.Questions[1] with { Files = new(1, 10, "camera") }, request.Questions[2]] });
        Assert.NotEmpty(result.Issues);
        Assert.Empty(result.Flows);
    }

    [Fact]
    public void Installation_size_limit_is_emitted_without_a_per_question_override()
    {
        var compiled = WhatsAppFlowCompiler.Compile(Request() with { MaxFileSizeMiB = 5 });
        Assert.Empty(compiled.Issues);
        using var document = JsonDocument.Parse(compiled.Flows[0].Json);
        Assert.Equal(5 * 1024, Controls(document.RootElement.GetProperty("screens")[1])[0].GetProperty("max-file-size-kb").GetInt32());
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { MaxFileSizeMiB = 26 }).Flows);
    }

    [Fact]
    public void Provider_diagnostics_use_the_question_identity_from_the_correct_screen()
    {
        var request = Request();
        var compiled = WhatsAppFlowCompiler.Compile(request).Flows.Single(flow => flow.Culture == "es");
        var projection = new ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppFlowProjection(1, compiled.Json, compiled.Hash,
            WhatsAppFlowCompiler.ProfileRevision, 1, "es", compiled.Mapping.Select(field => new ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppFlowProjectionField(
                field.FieldId, field.Key, field.Type, field.Required, field.MaxLength, field.Choices, field.Files)).ToArray());
        var photo = WhatsAppFlowDispatchService.Map(new(0, null, "max-uploaded-photos", 1), projection);
        Assert.Equal(request.Questions[1].FieldId, photo.FieldId);
        Assert.Equal("draft.fields[1].files.maxFiles", photo.Path);
        Assert.Equal("draft.submitLabel.es", WhatsAppFlowDispatchService.Map(new(2, null, "label", 2), projection).Path);
    }

    [Fact]
    public void Ten_files_fit_the_metadata_budget_without_relaxing_the_per_file_limit()
    {
        var question = Request().Questions[1] with { Files = new(1, 10, "camera") };
        Assert.Empty(WhatsAppFlowCompiler.Compile(Request() with { Questions = [question, Request().Questions[3] with { MaxLength = 100 }] }).Issues);
    }

    private static JsonElement[] Controls(JsonElement screen) => screen.GetProperty("layout").GetProperty("children")[0].GetProperty("children").EnumerateArray().ToArray();
    private static StructuredFormCheckRequest Request() => new(1, "es", Text("Visita"), Text("Confirmar"), [
        Question("shortText") with { MaxLength = 80 },
        Question("photo") with { Required = true, Files = new(1, 4, "camera") },
        Question("document") with { Files = new(0, 1) },
        Question("longText") with { MaxLength = 600 },
    ], ["es", "en"]);
    private static StructuredFormQuestion Question(string type)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text("Pregunta"), null, false, null, null);
    }
    private static Dictionary<string, string> Text(string value) => new Dictionary<string, string> { ["es"] = value, ["en"] = value };
}
