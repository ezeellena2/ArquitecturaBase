using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_edits_keep_distinct_form_usage_and_hide_it_without_form_read(bool filters)
    {
        using var writer = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        var unique = "Usage " + Guid.NewGuid().ToString("N");
        var texts = new { es = unique, en = unique };
        var definition = new { textColumn = "name", detailColumn = (string?)null, conditions = Array.Empty<object>(),
            excludeRecipient = false, sortColumn = "name", sortDescending = false };
        object create = filters ? new { name = texts, description = texts, filters = definition }
            : new { name = texts, description = texts, items = Array.Empty<object>() };
        using var created = await writer.PostAsJsonAsync("/api/forms/catalogs" + (filters ? "/filters" : ""), create, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();

        var form = Payload(unique);
        form["draft"]!["fields"] = new JsonArray(Enumerable.Range(1, 2).Select(index => (JsonNode)new JsonObject
        {
            ["localId"] = "catalog-" + index,
            ["type"] = "choice",
            ["label"] = new JsonObject { ["es"] = "Responsable " + index, ["en"] = "Assignee " + index },
            ["required"] = false,
            ["source"] = new JsonObject { ["catalog"] = catalogId },
        }).ToArray());
        await CreateAsync(writer, form);
        using var initial = await writer.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal(1, (await initial.ReadJsonAsync()).GetProperty("usageCount").GetInt32());

        var path = $"/api/forms/catalogs/{catalogId}" + (filters ? "/filters" : "");
        object Update(long revision) => filters
            ? new { expectedRevision = revision, name = texts, description = texts, filters = definition }
            : new { expectedRevision = revision, name = texts, description = texts, items = Array.Empty<object>() };
        using var saved = await writer.PutAsJsonAsync(path, Update(1), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var savedCatalog = (await saved.ReadJsonAsync()).GetProperty("catalog");
        Assert.Equal(1, savedCatalog.GetProperty("usageCount").GetInt32());
        Assert.Equal(2, savedCatalog.GetProperty("revision").GetInt64());

        using var catalogManager = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);
        using var hidden = await catalogManager.PutAsJsonAsync(path, Update(2), Ct);
        Assert.Equal(HttpStatusCode.OK, hidden.StatusCode);
        var hiddenCatalog = (await hidden.ReadJsonAsync()).GetProperty("catalog");
        Assert.Equal(JsonValueKind.Null, hiddenCatalog.GetProperty("usageCount").ValueKind);
        Assert.Equal(3, hiddenCatalog.GetProperty("revision").GetInt64());
        using var reloaded = await writer.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        Assert.Equal(1, (await reloaded.ReadJsonAsync()).GetProperty("usageCount").GetInt32());
    }
}
