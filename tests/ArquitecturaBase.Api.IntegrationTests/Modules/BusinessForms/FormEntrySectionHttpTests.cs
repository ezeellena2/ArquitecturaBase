using System.Net;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Section titles in the app (CW15-B) with PostgreSQL: the question where a section starts carries its title in the content
/// language, so the page opens a zone with it; the other questions carry nothing.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormEntrySectionHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_question_where_a_section_starts_carries_its_title_in_the_content_language()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        await scenario.SaveDraftAsync(payload =>
        {
            var fields = payload["draft"]!["fields"]!.AsArray();
            fields.Add(new JsonObject { ["localId"] = "lote", ["type"] = "shortText", ["label"] = new JsonObject { ["es"] = "Lote", ["en"] = "Lot" } });
            fields.Add(new JsonObject { ["localId"] = "dosis", ["type"] = "decimal", ["label"] = new JsonObject { ["es"] = "Dosis", ["en"] = "Dose" } });
            payload["draft"]!["sections"] = new JsonArray(
                new JsonObject { ["start"] = 1, ["title"] = new JsonObject { ["es"] = "Lote", ["en"] = "Lot" } },
                new JsonObject { ["start"] = 2, ["title"] = new JsonObject { ["es"] = "Productos", ["en"] = "Products" } });
        }, Ct);
        await scenario.PublishForPreparationAsync(factory, Ct);

        foreach (var (culture, titles) in new[] { ("es", new[] { "Lote", "Productos" }), ("en", ["Lot", "Products"]) })
        {
            using var client = scenario.Client(culture, scenario.FirstId);
            using var entry = await client.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
            Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
            if (culture == "es") await FormEntryContractCapture.WriteAsync(entry, "entry-sections-es", Ct);
            var questions = (await entry.ReadJsonAsync()).GetProperty("questions");
            Assert.False(questions[0].TryGetProperty("section", out _));
            Assert.Equal<string?>(titles, [questions[1].GetProperty("section").GetString(), questions[2].GetProperty("section").GetString()]);
        }
    }
}
