using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Multiple_selection_is_saved_with_its_range_and_a_large_manual_catalog_blocks_publication()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
            BusinessFormsPermissions.Publish, BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);
        await using var host = PublicationHost();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        var draft = Payload("Multiple authoring " + Guid.NewGuid().ToString("N"));
        draft["draft"]!["fields"] = new JsonArray(new JsonObject
        {
            ["localId"] = "multiple", ["type"] = "multiChoice", ["required"] = true,
            ["label"] = new JsonObject { ["es"] = "Riesgos", ["en"] = "Risks" },
            ["selection"] = new JsonObject { ["minSelected"] = 1, ["maxSelected"] = 3 },
            ["options"] = new JsonArray(Enumerable.Range(1, 3).Select(index => (JsonNode)new JsonObject
            {
                ["localId"] = "option-" + index,
                ["label"] = new JsonObject { ["es"] = "Riesgo " + index, ["en"] = "Risk " + index },
            }).ToArray()),
        });
        using var savedDraft = await client.PostAsJsonAsync("/api/forms/definitions", draft, Ct);
        var saved = await CaptureContractAsync(savedDraft, HttpStatusCode.Created, "multiple-choice", "draft");
        var ownField = Assert.Single(saved.GetProperty("definition").GetProperty("draft").GetProperty("fields").EnumerateArray());
        Assert.Equal("multiChoice", ownField.GetProperty("type").GetString());
        Assert.Equal(1, ownField.GetProperty("selection").GetProperty("minSelected").GetInt32());
        Assert.Equal(3, ownField.GetProperty("selection").GetProperty("maxSelected").GetInt32());
        Assert.Equal(3, ownField.GetProperty("options").GetArrayLength());
        Assert.All(ownField.GetProperty("options").EnumerateArray(), option => Assert.NotEqual(Guid.Empty, option.GetProperty("choiceId").GetGuid()));

        var catalogName = "Multiple catalog " + Guid.NewGuid().ToString("N");
        using var catalogResponse = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = catalogName, en = catalogName }, description = new { es = catalogName, en = catalogName },
            items = Enumerable.Range(1, 21).Select(index => new
            {
                localId = "catalog-" + index, code = "C" + index,
                text = new { es = "Opción " + index, en = "Option " + index }, isActive = true,
            }).ToArray(),
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, catalogResponse.StatusCode);
        var catalogId = (await catalogResponse.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var catalogDraft = Payload("Multiple catalog question " + Guid.NewGuid().ToString("N"));
        catalogDraft["draft"]!["fields"] = new JsonArray(new JsonObject
        {
            ["localId"] = "catalog-multiple", ["type"] = "multiChoice", ["required"] = true,
            ["label"] = new JsonObject { ["es"] = "Sectores", ["en"] = "Areas" },
            ["selection"] = new JsonObject { ["minSelected"] = 1, ["maxSelected"] = 3 },
            ["source"] = new JsonObject { ["catalog"] = catalogId.ToString("D") },
        });
        var catalogSaved = await CreateAsync(client, catalogDraft);
        var definition = catalogSaved.GetProperty("definition");
        var definitionId = definition.GetProperty("id").GetGuid();
        var fieldId = definition.GetProperty("draft").GetProperty("fields")[0].GetProperty("fieldId").GetGuid();
        using var checkResponse = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publication-checks", CheckPayload(catalogSaved), Ct);
        var check = await CaptureContractAsync(checkResponse, HttpStatusCode.OK, "multiple-choice", "catalog-too-large");
        Assert.False(check.GetProperty("canAccept").GetBoolean());
        var issue = Assert.Single(check.GetProperty("issues").EnumerateArray(), value =>
            value.GetProperty("code").GetString() == "Forms.Publication.CatalogTooLargeForMultiple");
        Assert.Equal(fieldId, issue.GetProperty("fieldId").GetGuid());
        Assert.Equal("draft.fields[0].source.catalog", issue.GetProperty("path").GetString());
    }
}
