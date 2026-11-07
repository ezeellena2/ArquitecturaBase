using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Choosing first by a field (LD1-A) with PostgreSQL: the draft saves the field, publishing checks it, the app names it, lists the
/// values the options have (a catalog field in its source's order and texts, a text field alphabetically) and offers only the
/// options of the value chosen, also while correcting. The send keeps taking any option of the question.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormEntryGroupByHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_supplier_is_chosen_after_its_zone_or_kind_and_publishing_checks_the_field()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        // A field that is not one of the catalog's is saved, and checking the publication blocks it on its path.
        await scenario.SaveDraftAsync(payload => payload["draft"]!["fields"] = new JsonArray(
            Question("by-zone", "Proveedor por zona", scenario.CatalogId, scenario.ZoneFieldId, true),
            Question("by-kind", "Proveedor por tipo", scenario.CatalogId, Guid.NewGuid(), false)), Ct);
        using var manager = scenario.Client();
        using var checkedDraft = await manager.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/publication-checks",
            CheckPayload(scenario), Ct);
        Assert.Equal(HttpStatusCode.OK, checkedDraft.StatusCode);
        var issue = Assert.Single((await checkedDraft.ReadJsonAsync()).GetProperty("issues").EnumerateArray(),
            value => value.GetProperty("path").GetString()!.StartsWith("draft.fields", StringComparison.Ordinal));
        Assert.Equal(FormPublicationErrors.GroupByInvalid.Code, issue.GetProperty("code").GetString());
        Assert.Equal("draft.fields[1].source.groupBy", issue.GetProperty("path").GetString());

        var saved = await scenario.SaveDraftAsync(payload => payload["draft"]!["fields"] = new JsonArray(
            Question("by-zone", "Proveedor por zona", scenario.CatalogId, scenario.ZoneFieldId, true),
            Question("by-kind", "Proveedor por tipo", scenario.CatalogId, scenario.KindFieldId, false)), Ct);
        var fields = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields");
        var byZone = fields[0].GetProperty("fieldId").GetGuid();
        var byKind = fields[1].GetProperty("fieldId").GetGuid();
        Assert.Equal(scenario.ZoneFieldId, fields[0].GetProperty("source").GetProperty("groupBy").GetGuid());

        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        using var entry = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        await FormEntryContractCapture.WriteAsync(entry, "entry-groupby-es", Ct);
        var questions = (await entry.ReadJsonAsync()).GetProperty("questions");
        Assert.Equal(scenario.ZoneFieldId, questions[0].GetProperty("catalog").GetProperty("groupBy").GetProperty("fieldId").GetGuid());
        Assert.Equal("zone", questions[0].GetProperty("catalog").GetProperty("groupBy").GetProperty("name").GetString());
        Assert.Equal(scenario.KindFieldId, questions[1].GetProperty("catalog").GetProperty("groupBy").GetProperty("fieldId").GetGuid());

        var root = $"/api/forms/entries/{scenario.DefinitionId}/versions/{versionId}/questions";
        using var zones = await first.GetAsync($"{root}/{byZone}/groups", Ct);
        Assert.Equal(HttpStatusCode.OK, zones.StatusCode);
        await FormEntryContractCapture.WriteAsync(zones, "groups-es", Ct);
        Assert.Equal([(scenario.CentralId.ToString("D"), "Centro"), (scenario.SouthId.ToString("D"), "Sur")], Groups(await zones.ReadJsonAsync()));
        using var kinds = await first.GetAsync($"{root}/{byKind}/groups", Ct);
        Assert.Equal([("cold", "cold"), ("hot", "hot")], Groups(await kinds.ReadJsonAsync()));

        using var central = await first.GetAsync($"{root}/{byZone}/options?pageSize=50&group={scenario.CentralId}", Ct);
        Assert.Equal(HttpStatusCode.OK, central.StatusCode);
        await FormEntryContractCapture.WriteAsync(central, "options-group-es", Ct);
        Assert.Equal(2, (await central.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var south = await first.GetAsync($"{root}/{byZone}/options?pageSize=50&group={scenario.SouthId}", Ct);
        Assert.Equal(scenario.SecondItemId, Assert.Single((await south.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var none = await first.GetAsync($"{root}/{byZone}/options?pageSize=50&withoutGroup=true", Ct);
        Assert.Equal(0, (await none.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var both = await first.GetAsync($"{root}/{byZone}/options?pageSize=50&group=cold&withoutGroup=true", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        using var anonymous = scenario.Host.CreateClient();
        using var unauthorized = await anonymous.GetAsync($"{root}/{byZone}/groups", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var editor = scenario.Client("es", scenario.CatalogEditorId);
        using var forbidden = await editor.GetAsync($"{root}/{byZone}/groups", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // The send takes any option of the question, and a correction lists the values of whoever answered.
        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}", Send(versionId, new { fieldId = byZone, value = scenario.SecondItemId.ToString("D") }), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        using var correcting = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{recordId}/versions/{versionId}/questions/{byZone}/groups", Ct);
        Assert.Equal(HttpStatusCode.OK, correcting.StatusCode);
        Assert.Equal(2, Groups(await correcting.ReadJsonAsync()).Length);
    }

    private static JsonObject Question(string localId, string label, Guid catalogId, Guid groupBy, bool required) => new()
    {
        ["localId"] = localId, ["type"] = "choice", ["required"] = required, ["label"] = new JsonObject { ["es"] = label, ["en"] = label },
        ["source"] = new JsonObject { ["catalog"] = catalogId.ToString("D"), ["groupBy"] = groupBy.ToString("D") },
    };

    // The saved draft as the editor sends it again: each question by its local id, without its sealed key.
    private static JsonObject CheckPayload(FormRecipientScenario scenario)
    {
        var check = scenario.CheckPayload();
        foreach (var (field, localId) in check["draft"]!["fields"]!.AsArray().Zip(new[] { "by-zone", "by-kind" }))
        {
            field!["localId"] = localId;
            field.AsObject().Remove("key");
        }
        return check;
    }

    private static (string? Id, string Text)[] Groups(JsonElement body) => body.GetProperty("groups").EnumerateArray()
        .Select(group => (group.GetProperty("id").GetString(), group.GetProperty("text").GetString()!)).ToArray();

    private static MultipartFormDataContent Send(Guid versionId, params object[] answers) => new()
    {
        { new StringContent(Guid.NewGuid().ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };
}
