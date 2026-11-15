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
}
