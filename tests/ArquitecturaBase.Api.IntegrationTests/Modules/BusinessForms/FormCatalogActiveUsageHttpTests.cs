using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Catalog_usage_keeps_the_active_version_when_the_draft_changes_its_catalog()
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage, BusinessFormsPermissions.Publish]);
        await using var host = PublicationHost();
        using var client = DeactivationClient(host, writer, "es");
        var unique = "ActiveUsage" + Guid.NewGuid().ToString("N")[..10];
        var firstCatalog = await CreateCatalogAsync("first");
        var secondCatalog = await CreateCatalogAsync("second");
        var form = Payload(unique);
        form["draft"]!["fields"] = CatalogFields(firstCatalog);
        var saved = await CreateAsync(client, form);
        var definitionId = saved.GetProperty("definition").GetProperty("id").GetGuid();
        await PublishAsync(saved);

        var edit = EditPayload(saved);
        edit["draft"]!["fields"] = CatalogFields(secondCatalog);
        using var edited = await client.PutAsJsonAsync($"/api/forms/definitions/{definitionId}/draft", edit, Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var changedDraft = await edited.ReadJsonAsync();

        await AssertUsageAsync(firstCatalog, 1);
        await AssertUsageAsync(secondCatalog, 1);
        await PublishAsync(changedDraft);
        await AssertUsageAsync(firstCatalog, 0);
        await AssertUsageAsync(secondCatalog, 1);

        async Task<Guid> CreateCatalogAsync(string suffix)
        {
            var name = unique + suffix;
            using var response = await client.PostAsJsonAsync("/api/forms/catalogs", new
            {
                name = new { es = name, en = name }, description = new { es = "Uso", en = "Usage" },
                items = new[] { new { localId = "one", code = "ONE", text = new { es = "Uno", en = "One" },
                    detail = new { es = (string?)null, en = (string?)null }, isActive = true } },
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        }

        async Task PublishAsync(System.Text.Json.JsonElement draft)
        {
            var attempt = await PublicationAttemptAsync(client, definitionId, draft);
            using var accepted = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/publications", attempt, Ct);
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            await CompletePublicationAsync(host, (await accepted.ReadJsonAsync()).GetProperty("operationId").GetGuid(), definitionId);
        }

        async Task AssertUsageAsync(Guid catalogId, int expected)
        {
            using var detail = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal(expected, (await detail.ReadJsonAsync()).GetProperty("usageCount").GetInt32());
        }

        static JsonArray CatalogFields(Guid catalogId) => new(new JsonObject
        {
            ["localId"] = "catalog", ["type"] = "choice", ["required"] = false,
            ["label"] = new JsonObject { ["es"] = "Catálogo", ["en"] = "Catalog" },
            ["source"] = new JsonObject { ["catalog"] = catalogId },
        });
    }
}
