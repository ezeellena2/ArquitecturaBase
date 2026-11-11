using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// W1-A: a Flow cannot ask for a location, so the location is asked before it and the Flow never shows it. Like a calculation, the
// question keeps its place in the sealed mapping, without a control or a key in the complete payload; the server fills it.
public sealed class WhatsAppFlowLocationCompilerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_location_question_keeps_its_place_in_the_mapping_and_leaves_the_flow_as_without_it(bool required)
    {
        var text = Question("shortText") with { MaxLength = 80 };
        var location = Question("location") with { Required = required };

        var result = WhatsAppFlowCompiler.Compile(Request(text, location));
        var without = Assert.Single(WhatsAppFlowCompiler.Compile(Request(text)).Flows);

        Assert.Empty(result.Issues);
        var flow = Assert.Single(result.Flows);
        Assert.Equal([text.FieldId, location.FieldId], flow.Mapping.Select(field => field.FieldId));
        Assert.Equal(("location", required), (flow.Mapping[1].Type, flow.Mapping[1].Required));
        Assert.Equal(without.Json, flow.Json);
        Assert.Equal(without.ComponentCount, flow.ComponentCount);
        Assert.DoesNotContain(location.Key, flow.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_form_asks_for_one_location_on_whatsapp()
    {
        var second = Question("location");

        var result = WhatsAppFlowCompiler.Compile(Request(Question("shortText") with { MaxLength = 80 }, Question("location"), second));

        Assert.Empty(result.Flows);
        var issue = Assert.Single(result.Issues);
        Assert.Equal((second.FieldId, "draft.fields", FlowErrors.UnsupportedConfiguration), (issue.FieldId, issue.Path, issue.Error));
    }

    [Fact]
    public void A_location_with_options_or_a_length_never_compiles()
    {
        var location = Question("location") with { MaxLength = 80 };

        var result = WhatsAppFlowCompiler.Compile(Request(Question("shortText") with { MaxLength = 80 }, location));

        Assert.Empty(result.Flows);
        Assert.Equal(location.FieldId, Assert.Single(result.Issues).FieldId);
    }

    private static StructuredFormCheckRequest Request(params StructuredFormQuestion[] questions) =>
        new(1, "es", Text("Visita"), Text("Confirmar"), questions, ["es"]);

    private static StructuredFormQuestion Question(string type)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text("Pregunta"), null, false, null, null);
    }

    private static Dictionary<string, string> Text(string value) => new() { ["es"] = value, ["en"] = value };
}
