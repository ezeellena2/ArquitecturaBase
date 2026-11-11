using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormCatalogEndpointsTests
{
    [Fact]
    public async Task Sorting_by_a_field_orders_every_option_across_pages_with_the_values_of_the_same_save()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Orden " + Guid.NewGuid().ToString("N"), en = "Order" };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = texts, description = texts,
            fields = new[] { FieldPayload(null, "integer") },
            items = new[] { "A", "B", "C", "D", "E" }.Select(code => Item(code.ToLowerInvariant(), null, code, "Opción " + code, null, true)).ToArray(),
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var catalogId = saved.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = saved.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        Guid Id(string code) => saved.GetProperty("itemIdentityMap").GetProperty(code.ToLowerInvariant()).GetGuid();

        // The values travel in the same save as the order: 9 sorts before 10 by value, D ties with A and stays after it, and C,
        // without a value, goes last.
        var sortedRequest = new
        {
            expectedRevision = 1, name = texts, description = texts,
            items = new[] { FieldItem("a", Id("A"), "A", fieldId, "10", "Opción A"), FieldItem("b", Id("B"), "B", fieldId, "9", "Opción B"),
                FieldItem("d", Id("D"), "D", fieldId, "10", "Opción D"), FieldItem("e", Id("E"), "E", fieldId, "-1", "Opción E") },
            sort = new { column = fieldId.ToString("D"), descending = false },
        };
        await ExportRequestBodyAsync(sortedRequest, "sorted-request");
        using var sorted = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", sortedRequest, Ct);
        Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);

        using var secondPage = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=2&pageSize=2&sort=position", Ct);
        Assert.Equal(new[] { "A", "D" }, (await secondPage.ReadJsonAsync()).GetProperty("items").EnumerateArray().Select(row => row.GetProperty("code").GetString()));
        using var all = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?sort=position", Ct);
        var rows = (await all.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { "E", "B", "A", "D", "C" }, rows.Select(row => row.GetProperty("code").GetString()));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(row => row.GetProperty("position").GetInt32()));

        using var unknown = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", new
        {
            expectedRevision = 2, name = texts, description = texts, items = Array.Empty<object>(),
            sort = new { column = Guid.CreateVersion7().ToString("D"), descending = true },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.True((await unknown.ReadJsonAsync()).GetProperty("errors").TryGetProperty("sort.column", out _));
    }
}
