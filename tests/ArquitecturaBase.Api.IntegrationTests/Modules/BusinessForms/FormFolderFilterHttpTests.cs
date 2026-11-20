using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// CP1-A with PostgreSQL: the folder filter of «Diseñar formularios» and «Catálogos» takes the subfolders before counting and paging,
// an item in two subfolders comes once, «Sin carpeta» takes what is in none, and the facets stay inside the folder. «Formularios» no
// longer filters by folder (CM3): its folders are the menu.
public sealed partial class FormFolderHttpTests
{
    [Fact]
    public async Task Designs_and_catalogs_filter_by_a_folder_with_its_subfolders_before_paging_and_without_repeating()
    {
        using var manager = await OperatorAsync(P.Read, P.Manage, P.CatalogsRead, P.CatalogsManage, P.RecordsRead);
        var top = await CreateAsync(manager, null, Unique("Ventas"));
        var north = await CreateAsync(manager, top.Id, "Norte");
        var south = await CreateAsync(manager, top.Id, "Sur");
        var other = await CreateAsync(manager, null, Unique("Otra"));
        var prefix = "Z" + Guid.NewGuid().ToString("N")[..8];
        var actor = Actor(manager);
        var inTop = await SeedFormAsync(actor, prefix + " Uno");
        var inNorth = await SeedFormAsync(actor, prefix + " Dos");
        var inBoth = await SeedFormAsync(actor, prefix + " Tres");
        var loose = await SeedFormAsync(actor, prefix + " Cuatro");
        var elsewhere = await SeedFormAsync(actor, prefix + " Cinco");
        await FileAsync(manager, "definitions", inTop, top.Id);
        await FileAsync(manager, "definitions", inNorth, north.Id);
        await FileAsync(manager, "definitions", inBoth, north.Id, south.Id);
        await FileAsync(manager, "definitions", elsewhere, other.Id);
        var catalogInSouth = await SeedCatalogAsync(prefix + " Rubros");
        var looseCatalog = await SeedCatalogAsync(prefix + " Sueltos");
        await FileAsync(manager, "catalogs", catalogInSouth, south.Id, north.Id);

        async Task<(Guid[] Ids, int Total)> PageAsync(string path, string items)
        {
            using var response = await manager.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.ReadJsonAsync();
            var page = items.Length == 0 ? json : json.GetProperty(items);
            return (page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray(), page.GetProperty("totalCount").GetInt32());
        }

        const string path = "/api/forms/definitions";
        var first = await PageAsync($"{path}?search={prefix}&folderId={top.Id}&pageSize=2", "");
        var second = await PageAsync($"{path}?search={prefix}&folderId={top.Id}&pageSize=2&page=2", "");
        Assert.Equal((2, 3), (first.Ids.Length, first.Total));
        Assert.Equal(new[] { inTop, inNorth, inBoth }.Order(), first.Ids.Concat(second.Ids).Order());
        Assert.Equal([inBoth], (await PageAsync($"{path}?search={prefix}&folderId={south.Id}", "")).Ids);
        Assert.Equal([loose], (await PageAsync($"{path}?search={prefix}&withoutFolder=true", "")).Ids);
        Assert.Equal(HttpStatusCode.BadRequest, (await manager.GetAsync($"{path}?folderId={top.Id}&withoutFolder=true", Ct)).StatusCode);
        Assert.Empty((await PageAsync($"{path}?search={prefix}&folderId={Guid.NewGuid()}", "")).Ids);
        using var facets = await manager.GetAsync($"/api/forms/definitions/facets?search={prefix}&folderId={top.Id}", Ct);
        var counts = await facets.ReadJsonAsync();
        Assert.Equal((3, 3, 0), (counts.GetProperty("active").GetInt32(), counts.GetProperty("published").GetInt32(), counts.GetProperty("drafts").GetInt32()));

        var overview = await PageAsync($"/api/forms/overview?search={prefix}", "forms");
        Assert.Equal(new[] { inTop, inNorth, inBoth, loose, elsewhere }.Order(), overview.Ids.Order());
        foreach (var filter in new[] { $"&folderId={north.Id}", "&withoutFolder=true", $"&folderId={top.Id}&withoutFolder=true" })
        {
            using var ignored = await manager.GetAsync($"/api/forms/overview?search={prefix}{filter}", Ct);
            Assert.Equal(HttpStatusCode.OK, ignored.StatusCode);
            var json = await ignored.ReadJsonAsync();
            Assert.Equal(overview.Ids.Order(), json.GetProperty("forms").GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).Order());
            Assert.Equal(5, json.GetProperty("filterCounts").GetProperty("active").GetInt32());
        }

        var catalogPage = await PageAsync($"/api/forms/catalogs?search={prefix}&folderId={top.Id}", "");
        Assert.Equal([catalogInSouth], catalogPage.Ids);
        Assert.Equal(1, catalogPage.Total);
        using var catalogsJson = await manager.GetAsync($"/api/forms/catalogs?search={prefix}&folderId={top.Id}", Ct);
        Assert.Equal(1, (await catalogsJson.ReadJsonAsync()).GetProperty("filterCounts").GetProperty("byKind").GetProperty("all").GetInt32());
        Assert.Equal([looseCatalog], (await PageAsync($"/api/forms/catalogs?search={prefix}&withoutFolder=true", "")).Ids);
    }
}
