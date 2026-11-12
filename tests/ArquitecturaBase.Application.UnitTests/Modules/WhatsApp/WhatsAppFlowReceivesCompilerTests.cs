using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

// W3 (UC1-A): what a text question receives when the person shares a location or a contact on their own travels to the sealed
// mapping, like a calculation, and the Flow does not change: the same bytes, hash and components with and without it.
public sealed class WhatsAppFlowReceivesCompilerTests
{
    private static Dictionary<string, string> Text(string spanish, string english) => new() { ["es"] = spanish, ["en"] = english };

    private static StructuredFormQuestion Question(string type, string label, string? receives = null)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, Text(label, label), null, false, type is "shortText" or "longText" ? 80 : null, null,
            Receives: receives);
    }

    private static StructuredFormCheckRequest Request(params StructuredFormQuestion[] questions) =>
        new(1, "es", Text("Entrega", "Delivery"), Text("Enviar", "Send"), questions, ["es", "en"]);

    private static StructuredFormQuestion[] Without(StructuredFormQuestion[] questions) =>
        questions.Select(question => question with { Receives = null }).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_question_that_receives_shared_content_compiles_the_same_flow_and_keeps_it_in_the_mapping(bool block)
    {
        StructuredFormQuestion[] questions = block
            ?
            [
                Question("shortText", "Dirección", "location"),
                Question("group", "Bultos") with { Required = true, Rows = new(1, 3) },
                Question("decimal", "Peso") with { Repeated = true },
                Question("longText", "Recibió", "contactName"),
                Question("shortText", "Teléfono", "contactPhone"),
            ]
            : [Question("shortText", "Dirección", "location"), Question("longText", "Recibió", "contactName"),
                Question("shortText", "Teléfono", "contactPhone"), Question("decimal", "Peso")];

        var result = WhatsAppFlowCompiler.Compile(Request(questions));
        var without = WhatsAppFlowCompiler.Compile(Request(Without(questions)));

        Assert.Empty(result.Issues);
        Assert.Equal(new[] { "en", "es" }, result.Flows.Select(flow => flow.Culture).Order(StringComparer.Ordinal));
        foreach (var flow in result.Flows)
        {
            var plain = without.Flows.Single(other => other.Culture == flow.Culture);
            Assert.Equal(plain.Json, flow.Json);
            Assert.Equal(plain.Hash, flow.Hash);
            Assert.Equal(plain.ComponentCount, flow.ComponentCount);
            Assert.Equal(questions.Select(question => question.Receives), flow.Mapping.Select(field => field.Receives));
            Assert.DoesNotContain("receives", flow.Json, StringComparison.Ordinal);
            Assert.All(plain.Mapping, field => Assert.DoesNotContain("receives", JsonSerializer.Serialize(field, JsonSerializerOptions.Web), StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Accepting_seals_what_each_question_receives_in_each_culture_projection()
    {
        var fixture = new WhatsAppFlowPublicationFixture();
        var (address, notes) = (Question("shortText", "Dirección", "location"), Question("shortText", "Notas"));
        var snapshot = Request(address, notes);
        var check = WhatsAppFlowCompiler.Compile(snapshot);
        var request = new StructuredFormPublicationRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), ["forms.read"], new string('c', 64), WhatsAppFlowCompiler.ProfileRevision,
            check.Flows.Select(flow => new StructuredFormPublicationCulture(flow.Culture,
                WhatsAppFlowPublicationPolicy.CompilationFingerprint(flow.Hash))).ToArray(), snapshot);

        var result = await fixture.CreateService().AcceptAsync(request, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "en", "es" }, fixture.Projections.Keys.Order(StringComparer.Ordinal));
        foreach (var projection in fixture.Projections.Values)
        {
            Assert.Equal(new string?[] { "location", null }, projection.Mapping.Select(field => field.Receives));
            Assert.Contains("\"receives\":\"location\"", JsonSerializer.Serialize(projection.Mapping[0], JsonSerializerOptions.Web), StringComparison.Ordinal);
            Assert.DoesNotContain("receives", JsonSerializer.Serialize(projection.Mapping[1], JsonSerializerOptions.Web), StringComparison.Ordinal);
        }
    }
}
