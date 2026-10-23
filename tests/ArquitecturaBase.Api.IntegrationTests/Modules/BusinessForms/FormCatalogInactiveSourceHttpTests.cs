using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormCatalogEndpointsTests
{
    [Fact]
    public async Task Deactivating_a_source_option_keeps_dependent_catalogs_editable_and_reimportable()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var sourceTexts = new { es = "Zonas " + Guid.NewGuid().ToString("N"), en = "Zones" };
        using var sourceResponse = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = sourceTexts, description = sourceTexts,
            items = new[] { Item("north", null, "NORTE", "Norte", "North", true), Item("south", null, "SUR", "Sur", "South", true) },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, sourceResponse.StatusCode);
        var source = await sourceResponse.ReadJsonAsync();
        var sourceId = source.GetProperty("catalog").GetProperty("id").GetGuid();
        var northId = source.GetProperty("itemIdentityMap").GetProperty("north").GetGuid();

        var texts = new { es = "Sucursales " + Guid.NewGuid().ToString("N"), en = "Branches" };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = texts, description = texts, fields = new[] { FieldPayload(null, "catalog", sourceId) },
            items = new[] { Item("one", null, "ONE", "Centro", null, true) },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalog = await created.ReadJsonAsync();
        var id = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = catalog.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        var itemId = catalog.GetProperty("itemIdentityMap").GetProperty("one").GetGuid();
        var path = $"/api/forms/catalogs/{id}";
        object Update(long revision, params object[] items) => new
        {
            expectedRevision = revision, name = texts, description = texts, fields = new[] { FieldPayload(fieldId, "catalog", sourceId) }, items,
        };
        using var withNorth = await client.PutAsJsonAsync(path, Update(1, FieldItem("one", itemId, "ONE", fieldId, northId.ToString("D"), "Centro")), Ct);
        Assert.Equal(HttpStatusCode.OK, withNorth.StatusCode);

        using var deactivated = await client.PutAsJsonAsync($"/api/forms/catalogs/{sourceId}", new
        {
            expectedRevision = 1, name = sourceTexts, description = sourceTexts, items = new[] { Item("north", northId, "NORTE", "Norte", "North", false) },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        using var renamed = await client.PutAsJsonAsync(path, Update(2, FieldItem("one", itemId, "ONE", fieldId, northId.ToString("D"), "Centro renombrado")), Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(3, (await renamed.ReadJsonAsync()).GetProperty("catalog").GetProperty("revision").GetInt64());

        using var added = await client.PutAsJsonAsync(path, Update(3, FieldItem("two", null, "TWO", fieldId, northId.ToString("D"), "Nueva")), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, added.StatusCode);
        var invalid = await added.ReadJsonAsync();
        Assert.Equal("Forms.Catalog.FieldValueInvalid", invalid.GetProperty("code").GetString());
        Assert.True(invalid.GetProperty("errors").TryGetProperty("items[0].fields." + fieldId.ToString("D"), out _));

        using var exported = await client.GetAsync(path + "/export", Ct);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        var workbook = await exported.Content.ReadAsByteArrayAsync(Ct);
        using var previewBody = CatalogImportContent(workbook, "export.xlsx", 3);
        using var preview = await client.PostAsync(path + "/import-previews", previewBody, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.ReadJsonAsync();
        Assert.Empty(plan.GetProperty("issues").EnumerateArray());
        var counts = plan.GetProperty("counts");
        Assert.Equal(plan.GetProperty("rowsRead").GetInt32(), counts.GetProperty("unchanged").GetInt32());
        Assert.Equal(1, counts.GetProperty("unchanged").GetInt32());
        foreach (var key in new[] { "added", "changed", "deactivated", "reactivated", "errorRows" }) Assert.Equal(0, counts.GetProperty(key).GetInt32());

        using var applyBody = CatalogImportContent(workbook, "export.xlsx", 3, sha: plan.GetProperty("sheetSha256").GetString());
        using var applied = await client.PostAsync(path + "/imports", applyBody, Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(3, (await applied.ReadJsonAsync()).GetProperty("catalog").GetProperty("revision").GetInt64());
    }
}
