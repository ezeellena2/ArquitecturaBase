using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// A list that depends on another answer (CW7) with PostgreSQL: the draft names a new question by its local id and seals its
/// identity, publishing warns that WhatsApp cannot send it, and the app waits for the answer, lists only its options and checks
/// the chosen one when sending.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormEntryDependentListHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_supplier_list_follows_the_chosen_zone_in_the_app_and_is_not_offered_to_a_channel()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var zoneLocal = Guid.NewGuid().ToString("D");
        var saved = await scenario.SaveDraftAsync(payload => payload["draft"]!["fields"] = new JsonArray(
            new JsonObject { ["localId"] = zoneLocal, ["type"] = "choice", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Zona", ["en"] = "Zone" }, ["source"] = new JsonObject { ["catalog"] = scenario.ZoneCatalogId.ToString("D") } },
            new JsonObject { ["localId"] = "supplier-by-zone", ["type"] = "choice", ["required"] = true,
                ["label"] = new JsonObject { ["es"] = "Proveedor", ["en"] = "Supplier" }, ["source"] = new JsonObject
                {
                    ["catalog"] = scenario.CatalogId.ToString("D"),
                    ["match"] = new JsonArray(new JsonObject { ["fieldId"] = scenario.ZoneFieldId.ToString("D"), ["questionId"] = zoneLocal }),
                } }), Ct);
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var zoneId = fields[0].GetProperty("fieldId").GetGuid();
        var supplierId = fields[1].GetProperty("fieldId").GetGuid();
        Assert.Equal(zoneId, fields[1].GetProperty("source").GetProperty("match")[0].GetProperty("questionId").GetGuid());

        using var manager = scenario.Client();
        var check = scenario.CheckPayload();
        check["draft"]!["fields"]![1]!["localId"] = "supplier-by-zone";
        check["draft"]!["fields"]![1]!.AsObject().Remove("key");
        using var checkedDraft = await manager.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/publication-checks", check, Ct);
        Assert.Equal(HttpStatusCode.OK, checkedDraft.StatusCode);
        var publication = await checkedDraft.ReadJsonAsync();
        Assert.DoesNotContain(publication.GetProperty("issues").EnumerateArray(), issue => issue.GetProperty("path").GetString()!.StartsWith("draft.fields", StringComparison.Ordinal));
        var warning = Assert.Single(publication.GetProperty("warnings").EnumerateArray());
        Assert.Equal(FormPublicationErrors.DependsOnAnswer.Code, warning.GetProperty("code").GetString());
        Assert.Equal(supplierId, warning.GetProperty("fieldId").GetGuid());
        Assert.Equal("draft.fields[1].source.match", warning.GetProperty("path").GetString());

        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        using var entry = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        await FormEntryContractCapture.WriteAsync(entry, "entry-depends-es", Ct);
        var questions = (await entry.ReadJsonAsync()).GetProperty("questions");
        Assert.False(questions[0].GetProperty("catalog").TryGetProperty("dependsOn", out _));
        var dependent = questions[1].GetProperty("catalog");
        Assert.Equal([zoneId], dependent.GetProperty("dependsOn").EnumerateArray().Select(id => id.GetGuid()));
        Assert.Equal(JsonValueKind.Null, dependent.GetProperty("onlyOption").ValueKind);
        Assert.Equal(JsonValueKind.Null, dependent.GetProperty("issue").ValueKind);

        var optionsPath = $"/api/forms/entries/{scenario.DefinitionId}/versions/{versionId}/questions/{supplierId}/options";
        using var central = await first.GetAsync($"{optionsPath}?pageSize=50&answers[{zoneId}]={scenario.CentralId}", Ct);
        Assert.Equal(HttpStatusCode.OK, central.StatusCode);
        await FormEntryContractCapture.WriteAsync(central, "options-depends-es", Ct);
        var page = await central.ReadJsonAsync();
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
        Assert.Contains(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == scenario.FirstItemId);
        Assert.DoesNotContain(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == scenario.SecondItemId);
        using var waiting = await first.GetAsync(optionsPath, Ct);
        Assert.Equal(HttpStatusCode.Conflict, waiting.StatusCode);
        Assert.Equal(FormRecordErrors.CatalogDependsOnAnswer.Code, (await waiting.ReadJsonAsync()).GetProperty("code").GetString());

        using var mismatch = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(versionId, Choice(zoneId, scenario.SouthId), Choice(supplierId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        var problem = await mismatch.ReadJsonAsync();
        Assert.Equal(FormRecordErrors.CatalogItemInactive.Code, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("answers." + supplierId.ToString("D"), out _));
        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(versionId, Choice(zoneId, scenario.CentralId), Choice(supplierId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);

        var forms = await scenario.ScopeAsync(services => services.GetRequiredService<IFormSelectionReader>()
            .ListAsync("es", new ListStructuredFormsRequest { PageSize = 100 }, Ct));
        var listed = Assert.Single(forms.Items, form => form.DefinitionId == scenario.DefinitionId);
        Assert.Equal("Proveedor", listed.DependentQuestion!["es"]);
        Assert.Equal("Supplier", listed.DependentQuestion["en"]);
    }

    private static object Choice(Guid fieldId, Guid itemId) => new { fieldId, value = itemId.ToString("D") };

    private static MultipartFormDataContent Send(Guid versionId, params object[] answers) => new()
    {
        { new StringContent(Guid.NewGuid().ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };
}
