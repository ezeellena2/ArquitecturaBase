using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppManualFlowPayloadTests
{
    private static readonly WhatsAppOptions Settings = new() { WabaId = "1", PhoneNumberId = "2", GraphApiVersion = "v25.0" };

    [Theory]
    [InlineData("es", false)]
    [InlineData("en", false)]
    [InlineData("es", true)]
    [InlineData("en", true)]
    public void Empty_partial_and_complete_prefill_preserve_the_same_compiled_flow_and_catalog_navigation(
        string culture, bool catalogAfterPhoto)
    {
        var text = Question(1, "shortText") with { MaxLength = 80, Required = true };
        var catalog = Question(2, "choice") with { SourceCatalogId = Id(5), Required = true };
        var photo = Question(3, "photo") with { Files = new(1, 1, "camera"), Required = true };
        var document = Question(4, "document") with { Files = new(0, 1) };
        var request = new StructuredFormCheckRequest(1, "es", Text("Prueba", "Test"), Text("Enviar", "Submit"),
            catalogAfterPhoto ? [text, photo, catalog, document] : [text, catalog, photo, document], ["es", "en"]);
        var compilation = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(compilation.Issues);
        var compiled = Assert.Single(compilation.Flows, flow => flow.Culture == culture);
        var bytes = Encoding.UTF8.GetBytes(compiled.Json);
        var projection = new WhatsAppFlowProjection(1, compiled.Json, compiled.Hash, WhatsAppFlowCompiler.ProfileRevision,
            WhatsAppFlowCompiler.CodecVersion, culture, compiled.Mapping.Select(field => new WhatsAppFlowProjectionField(
                field.FieldId, field.Key, field.Type, field.Required, field.MaxLength, field.Choices, field.Files)).ToArray());
        using var published = JsonDocument.Parse(compiled.Json);
        var screens = published.RootElement.GetProperty("screens");
        Assert.Equal(3, screens.GetArrayLength());
        var mapKeys = catalogAfterPhoto ? new[] { "prefill_FORM", "prefill_FORM_A" } : ["prefill_FORM"];
        var publishedData = screens[0].GetProperty("data");
        var catalogControl = Assert.Single(screens[catalogAfterPhoto ? 1 : 0].GetProperty("layout").GetProperty("children")[0]
            .GetProperty("children").EnumerateArray(), control => control.TryGetProperty("name", out var name) && name.GetString() == catalog.Key);
        Assert.Equal(catalogAfterPhoto ? "${screen.FORM.data." + catalog.Key + "_options}" : "${data." + catalog.Key + "_options}",
            catalogControl.GetProperty("data-source").GetString());
        Assert.Equal("array", publishedData.GetProperty(catalog.Key + "_options").GetProperty("type").GetString());
        Assert.Equal(mapKeys, publishedData.EnumerateObject().Where(property => property.Name.StartsWith("prefill_", StringComparison.Ordinal)).Select(property => property.Name));
        for (var index = 0; index < screens.GetArrayLength(); index++)
        {
            var form = screens[index].GetProperty("layout").GetProperty("children")[0];
            if (index == 0 || catalogAfterPhoto && index == 1)
            {
                var mapKey = mapKeys[index];
                Assert.Equal(index == 0 ? "${data." + mapKey + "}" : "${screen.FORM.data." + mapKey + "}",
                    form.GetProperty("init-values").GetString());
                Assert.Equal("object", publishedData.GetProperty(mapKey).GetProperty("type").GetString());
            }
            else Assert.False(form.TryGetProperty("init-values", out _));
        }
        var optionId = Id(6);
        StructuredFormCatalogOffer[] offers = [new(catalog.FieldId, catalog.Key, catalog.Label[culture],
            catalog.SourceCatalogId!.Value, 1, "Manual", culture == "es" ? "Equipos" : "Equipment",
            [new(optionId, culture == "es" ? "Equipo de prueba" : "Test equipment", culture == "es" ? "Detalle de prueba" : "Test detail")])];
        Dictionary<string, string>[] selections = [
            new(StringComparer.Ordinal),
            new(StringComparer.Ordinal) { [text.Key] = culture == "es" ? "Lugar de prueba" : "Test location" },
            new(StringComparer.Ordinal) { [text.Key] = culture == "es" ? "Lugar de prueba" : "Test location", [catalog.Key] = optionId.ToString("D") },
        ];
        JsonObject? baseline = null;
        foreach (var selection in selections)
        {
            var initial = WhatsAppFlowPrefillPolicy.Build(projection, selection);
            Assert.True(initial.IsSuccess);
            var message = new WhatsAppFlowMessageResponse(PhoneNumber.Create("+12025550123").Value,
                "Synthetic form message", "Open", "123", "synthetic-flow-token", screens[0].GetProperty("id").GetString()!,
                new("1", "2", "v25.0", "synthetic-configuration"), offers, initial.Value);
            var parameters = WhatsAppMessagePayload.Build(message, Settings)["interactive"]!["action"]!["parameters"]!.AsObject();
            Assert.Equal("123", parameters["flow_id"]!.GetValue<string>());
            Assert.Equal("navigate", parameters["flow_action"]!.GetValue<string>());
            var navigation = parameters["flow_action_payload"]!.AsObject();
            Assert.Equal("FORM", navigation["screen"]!.GetValue<string>());
            var data = navigation["data"]!.AsObject();
            var expectedMaps = mapKeys.Where(key => publishedData.GetProperty(key).GetProperty("properties")
                .EnumerateObject().Any(property => selection.ContainsKey(property.Name))).ToArray();
            Assert.Equal(expectedMaps, initial.Value.Keys);
            Assert.Equal(expectedMaps.Length + 1, data.Count);
            var actual = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mapKey in mapKeys)
            {
                if (!expectedMaps.Contains(mapKey, StringComparer.Ordinal))
                {
                    Assert.False(data.ContainsKey(mapKey));
                    continue;
                }
                var fields = Assert.IsType<JsonObject>(data[mapKey]);
                Assert.NotEmpty(fields);
                foreach (var field in fields)
                {
                    Assert.True(publishedData.GetProperty(mapKey).GetProperty("properties").TryGetProperty(field.Key, out _));
                    Assert.NotNull(field.Value);
                    actual.Add(field.Key, field.Value.GetValue<string>());
                }
                Assert.True(data.Remove(mapKey));
            }
            Assert.Equal(selection.OrderBy(field => field.Key, StringComparer.Ordinal), actual.OrderBy(field => field.Key, StringComparer.Ordinal));
            Assert.DoesNotContain(photo.Key, data.ToJsonString(), StringComparison.Ordinal);
            Assert.DoesNotContain(document.Key, data.ToJsonString(), StringComparison.Ordinal);
            var options = Assert.IsType<JsonArray>(data[catalog.Key + "_options"]);
            var offered = Assert.Single(options)!;
            Assert.Equal(optionId.ToString("D"), offered["id"]!.GetValue<string>());
            Assert.Equal(offers[0].Options[0].Text, offered["title"]!.GetValue<string>());
            Assert.Equal(offers[0].Options[0].Detail, offered["description"]!.GetValue<string>());
            if (baseline is null) baseline = parameters.DeepClone().AsObject();
            else Assert.True(JsonNode.DeepEquals(baseline, parameters));
            Assert.Equal(bytes, Encoding.UTF8.GetBytes(projection.Json));
            Assert.Equal(compiled.Hash, projection.Hash);
            Assert.Equal(compiled.Hash, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void A_compiled_text_and_photo_flow_without_prefill_or_catalogs_omits_navigation_data(string culture)
    {
        var request = new StructuredFormCheckRequest(1, "es", Text("Prueba", "Test"), Text("Enviar", "Submit"),
            [Question(1, "shortText") with { MaxLength = 80, Required = true },
                Question(3, "photo") with { Files = new(1, 1, "camera"), Required = true }], ["es", "en"]);
        var compilation = WhatsAppFlowCompiler.Compile(request);
        Assert.Empty(compilation.Issues);
        var compiled = Assert.Single(compilation.Flows, flow => flow.Culture == culture);
        var projection = new WhatsAppFlowProjection(1, compiled.Json, compiled.Hash, WhatsAppFlowCompiler.ProfileRevision,
            WhatsAppFlowCompiler.CodecVersion, culture, compiled.Mapping.Select(field => new WhatsAppFlowProjectionField(
                field.FieldId, field.Key, field.Type, field.Required, field.MaxLength, field.Choices, field.Files)).ToArray());
        using var published = JsonDocument.Parse(compiled.Json);
        var firstScreen = published.RootElement.GetProperty("screens")[0];
        Assert.Equal("${data.prefill_FORM}", firstScreen.GetProperty("layout").GetProperty("children")[0].GetProperty("init-values").GetString());
        var initial = WhatsAppFlowPrefillPolicy.Build(projection, new Dictionary<string, string>(StringComparer.Ordinal));
        Assert.True(initial.IsSuccess);
        Assert.Empty(initial.Value);
        var message = new WhatsAppFlowMessageResponse(PhoneNumber.Create("+12025550123").Value,
            "Synthetic form message", "Open", "123", "synthetic-flow-token", firstScreen.GetProperty("id").GetString()!,
            new("1", "2", "v25.0", "synthetic-configuration"), initialValues: initial.Value);
        var parameters = WhatsAppMessagePayload.Build(message, Settings)["interactive"]!["action"]!["parameters"]!.AsObject();
        Assert.Equal("123", parameters["flow_id"]!.GetValue<string>());
        var navigation = parameters["flow_action_payload"]!.AsObject();
        Assert.Equal("FORM", navigation["screen"]!.GetValue<string>());
        Assert.False(navigation.ContainsKey("data"));
        Assert.Single(navigation);
        Assert.Equal(compiled.Json, projection.Json);
        Assert.Equal(compiled.Hash, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(projection.Json))));
    }

    private static Guid Id(int value) => Guid.Parse("019a0000-0000-7000-8000-" + value.ToString("D12", System.Globalization.CultureInfo.InvariantCulture));
    private static StructuredFormQuestion Question(int id, string type) =>
        new(Id(id), "field_" + Id(id).ToString("N"), type, Text("Campo", "Field"), null, false, null, null);
    private static Dictionary<string, string> Text(string spanish, string english) => new() { ["es"] = spanish, ["en"] = english };
}