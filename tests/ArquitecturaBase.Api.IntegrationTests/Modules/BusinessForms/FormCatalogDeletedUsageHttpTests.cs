using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Catalog_usage_excludes_deleted_forms_but_keeps_deactivated_forms_and_counts_each_definition_once()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, writer, "es");
        var name = "UsageDelete" + Guid.NewGuid().ToString("N")[..10];
        using var catalog = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = name, en = name }, description = new { es = "Uso", en = "Usage" },
            items = new[] { new { localId = "one", code = "ONE", text = new { es = "Uno", en = "One" },
                detail = new { es = (string?)null, en = (string?)null }, isActive = true } },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, catalog.StatusCode);
        var catalogId = (await catalog.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var formIds = new List<Guid>();
        foreach (var index in new[] { 1, 2 })
        {
            var form = Payload(name + index);
            form["draft"]!["fields"] = new JsonArray(Enumerable.Range(1, 2).Select(field => (JsonNode)new JsonObject
            {
                ["localId"] = "catalog-" + field,
                ["type"] = "choice",
                ["label"] = new JsonObject { ["es"] = "Catálogo " + field, ["en"] = "Catalog " + field },
                ["required"] = false,
                ["source"] = new JsonObject { ["catalog"] = catalogId },
            }).ToArray());
            var saved = await CreateAsync(client, form);
            var id = saved.GetProperty("definition").GetProperty("id").GetGuid();
            formIds.Add(id);
            if (index != 2) continue;
            var attempt = await PublicationAttemptAsync(client, id, saved);
            using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{id}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), id);
        }
        using var initial = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal(2, (await initial.ReadJsonAsync()).GetProperty("usageCount").GetInt32());

        using var deleted = await DeleteDefinitionAsync(client, formIds[0], 1);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var deactivated = await client.PostAsJsonAsync($"/api/forms/definitions/{formIds[1]}/deactivation", new { expectedRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        using var detail = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(1, (await detail.ReadJsonAsync()).GetProperty("usageCount").GetInt32());
        using var list = await client.GetAsync("/api/forms/catalogs?search=" + name, Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(1, Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("usageCount").GetInt32());
    }
}
