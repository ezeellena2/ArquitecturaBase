using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// UB2-A: a Flow cannot ask for a location, so the compiler never takes one, even if something let it through.
public sealed class WhatsAppFlowLocationCompilerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_location_question_never_compiles_into_a_flow(bool required)
    {
        var location = Question("location") with { Required = required };
        var request = new StructuredFormCheckRequest(1, "es", Text("Visita"), Text("Confirmar"),
            [Question("shortText") with { MaxLength = 80 }, location], ["es"]);

        var result = WhatsAppFlowCompiler.Compile(request);

        Assert.Empty(result.Flows);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(location.FieldId, issue.FieldId);
        Assert.Equal("draft.fields", issue.Path);
    }

    private static StructuredFormQuestion Question(string type)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text("Pregunta"), null, false, null, null);
    }

    private static Dictionary<string, string> Text(string value) => new() { ["es"] = value, ["en"] = value };
}
