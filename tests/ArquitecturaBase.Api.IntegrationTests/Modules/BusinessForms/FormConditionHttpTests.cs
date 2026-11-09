using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Conditions (CO1-A) with PostgreSQL: the draft seals the question and the new options a condition names, the entry describes
/// it, a hidden question is neither required nor accepted and a shown one keeps its rules.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormConditionHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Kind = "0196f2a0-0000-7000-8000-0000000000d1";

    [Fact]
    public async Task A_question_shows_only_with_the_answers_its_condition_names()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var saved = await scenario.SaveDraftAsync(payload =>
        {
            var fields = payload["draft"]!["fields"]!.AsArray();
            fields.Add(new JsonObject { ["localId"] = Kind, ["type"] = "choice", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Concepto", ["en"] = "Concept" },
                ["options"] = new JsonArray(
                    new JsonObject { ["localId"] = "goods", ["label"] = new JsonObject { ["es"] = "Productos", ["en"] = "Goods" } },
                    new JsonObject { ["localId"] = "services", ["label"] = new JsonObject { ["es"] = "Servicios", ["en"] = "Services" } }) });
            fields.Add(new JsonObject { ["localId"] = "from", ["type"] = "date", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Período desde", ["en"] = "Period from" },
                ["showWhen"] = new JsonObject { ["questionId"] = Kind, ["values"] = new JsonArray("services") } });
        }, Ct);
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var kindId = fields[1].GetProperty("fieldId").GetGuid();
        var fromId = fields[2].GetProperty("fieldId").GetGuid();
        var goods = fields[1].GetProperty("options")[0].GetProperty("choiceId").GetGuid();
        var services = fields[1].GetProperty("options")[1].GetProperty("choiceId").GetGuid();
        Assert.Equal(kindId, fields[2].GetProperty("showWhen").GetProperty("questionId").GetGuid());
        Assert.Equal(services.ToString("D"), fields[2].GetProperty("showWhen").GetProperty("values")[0].GetString());

        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var client = scenario.Client("es", scenario.FirstId);
        using var entry = await client.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        Assert.Equal(kindId, (await entry.ReadJsonAsync()).GetProperty("questions")[2].GetProperty("showWhen").GetProperty("questionId").GetGuid());

        var supplier = new { fieldId = scenario.QuestionId, value = scenario.FirstItemId.ToString("D") };
        using var hidden = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(versionId, supplier, new { fieldId = kindId, value = goods.ToString("D") }), Ct);
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        using var forged = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(versionId, supplier, new { fieldId = kindId, value = goods.ToString("D") }, new { fieldId = fromId, value = "2026-11-01" }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        using var missing = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(versionId, supplier, new { fieldId = kindId, value = services.ToString("D") }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using var shown = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(versionId, supplier, new { fieldId = kindId, value = services.ToString("D") }, new { fieldId = fromId, value = "2026-11-01" }), Ct);
        Assert.Equal(HttpStatusCode.OK, shown.StatusCode);
    }

    private static MultipartFormDataContent Send(Guid versionId, params object[] answers) => new()
    {
        { new StringContent(Guid.NewGuid().ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };
}
