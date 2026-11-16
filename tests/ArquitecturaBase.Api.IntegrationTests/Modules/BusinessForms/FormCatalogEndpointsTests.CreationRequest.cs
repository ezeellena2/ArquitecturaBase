using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormCatalogEndpointsTests
{
    // F24: a creation that repeats its request id (a lost response, a double click that crosses) answers with the catalog the first made.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_repeated_creation_request_answers_with_the_same_catalog(bool filters)
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var name = "Repeated " + Guid.NewGuid().ToString("N");
        var texts = new { es = name, en = name };
        var requestId = Guid.NewGuid();
        var definition = new { textColumn = "name", detailColumn = (string?)null, conditions = Array.Empty<object>(), excludeRecipient = false, sortColumn = "name", sortDescending = false };
        object body = filters ? new { requestId, name = texts, description = texts, filters = definition }
            : new
            {
                requestId, name = texts, description = texts,
                items = new[] { Item("first", null, "ONE", "Uno", null, true), Item("second", null, "TWO", "Dos", null, true) },
            };
        var path = "/api/forms/catalogs" + (filters ? "/filters" : "");
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.PostAsJsonAsync(path, body, Ct)));
        using var later = await client.PostAsJsonAsync(path, body, Ct);
        try
        {
            Assert.All(responses.Append(later), response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
            var saved = new List<System.Text.Json.JsonElement>();
            foreach (var response in responses.Append(later)) saved.Add(await response.ReadJsonAsync());
            var id = saved[0].GetProperty("catalog").GetProperty("id").GetGuid();
            Assert.All(saved, value => Assert.Equal(id, value.GetProperty("catalog").GetProperty("id").GetGuid()));
            if (!filters)
                Assert.All(saved, value => Assert.Equal(saved[0].GetProperty("itemIdentityMap").GetRawText(), value.GetProperty("itemIdentityMap").GetRawText()));
            Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().CountAsync(catalog => catalog.CreationRequestId == requestId, Ct)));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
        // Another request id is another catalog, with the same name.
        using var other = await client.PostAsJsonAsync(path, filters
            ? new { requestId = Guid.NewGuid(), name = texts, description = texts, filters = definition }
            : (object)new { requestId = Guid.NewGuid(), name = texts, description = texts, items = new[] { Item("first", null, "ONE", "Uno", null, true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    // F24: the same request id with other content, or over a catalog that changed since, is a conflict; it never answers the old catalog.
    [Fact]
    public async Task A_repeated_creation_request_with_other_content_or_a_changed_catalog_is_a_conflict()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var name = "Conflict " + Guid.NewGuid().ToString("N");
        var texts = new { es = name, en = name };
        var requestId = Guid.NewGuid();
        object Body(string code, string fieldKey = "field") => new
        {
            requestId, name = texts, description = texts, fields = new[] { FieldPayload(null, "text", key: fieldKey) },
            items = new[] { Item("first", null, code, "Uno", null, true), Item("second", null, "TWO", "Dos", null, true) },
        };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", Body("ONE"), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        using var same = await client.PostAsJsonAsync("/api/forms/catalogs", Body("ONE"), Ct);
        Assert.Equal(HttpStatusCode.Created, same.StatusCode);
        var again = await same.ReadJsonAsync();
        Assert.Equal(saved.GetProperty("fieldIdentityMap").GetRawText(), again.GetProperty("fieldIdentityMap").GetRawText());
        Assert.Equal(saved.GetProperty("itemIdentityMap").GetRawText(), again.GetProperty("itemIdentityMap").GetRawText());
        foreach (var other in new[] { Body("OTHER"), Body("ONE", "another") })
        {
            using var conflict = await client.PostAsJsonAsync("/api/forms/catalogs", other, Ct);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }
        // Missing items or a missing local id are a validation error, not a failure of the server.
        using var withoutItems = await client.PostAsJsonAsync("/api/forms/catalogs", new { requestId, name = texts, description = texts }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, withoutItems.StatusCode);
        using var withoutLocalId = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            requestId, name = texts, description = texts, items = new[] { new { code = "ONE", text = new { es = "Uno" }, isActive = true } },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, withoutLocalId.StatusCode);
        // After a change of the catalog, the request is no longer the one that made it.
        var catalogId = saved.GetProperty("catalog").GetProperty("id").GetGuid();
        using var changed = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", new
        {
            expectedRevision = 1, name = new { es = name + " 2", en = name }, description = texts, items = Array.Empty<object>(),
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var late = await client.PostAsJsonAsync("/api/forms/catalogs", Body("ONE"), Ct);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    // F24: the filters catalog answers a repeated request with the filters it saved, and a request id that made another kind is a conflict.
    [Fact]
    public async Task A_repeated_filters_creation_request_answers_the_saved_filters_and_refuses_another_kind()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var name = "Filters " + Guid.NewGuid().ToString("N");
        var texts = new { es = name, en = name };
        var requestId = Guid.NewGuid();
        object Definition(bool descending) => new { textColumn = "name", detailColumn = (string?)null, conditions = Array.Empty<object>(), excludeRecipient = false, sortColumn = "name", sortDescending = descending };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs/filters", new { requestId, name = texts, description = texts, filters = Definition(false) }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var repeated = await client.PostAsJsonAsync("/api/forms/catalogs/filters", new { requestId, name = texts, description = texts, filters = Definition(true) }, Ct);
        Assert.Equal(HttpStatusCode.Created, repeated.StatusCode);
        Assert.False((await repeated.ReadJsonAsync()).GetProperty("catalog").GetProperty("filters").GetProperty("sortDescending").GetBoolean());
        var manual = Guid.NewGuid();
        using var made = await client.PostAsJsonAsync("/api/forms/catalogs", new { requestId = manual, name = new { es = name + " manual", en = name }, description = texts, items = new[] { Item("first", null, "ONE", "Uno", null, true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, made.StatusCode);
        using var crossed = await client.PostAsJsonAsync("/api/forms/catalogs/filters", new { requestId = manual, name = texts, description = texts, filters = Definition(false) }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, crossed.StatusCode);
    }
}
