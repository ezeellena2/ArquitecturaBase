using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// CM2 with PostgreSQL: «Agregar formularios» reads the forms directly in a folder (drafts and deactivated ones, not the deleted ones)
// and replaces their set against the one it read, without touching its catalogs, its subfolders or the other folders of each form.
public sealed partial class FormFolderHttpTests
{
    [Fact]
    public async Task The_forms_of_a_folder_are_read_and_replaced_against_the_set_that_was_read()
    {
        using var manager = await OperatorAsync(P.Read, P.Manage, P.CatalogsRead, P.CatalogsManage);
        var folder = await CreateAsync(manager, null, Unique("Compras"), "package");
        var group = await CreateAsync(manager, folder.Id, "Insumos");
        var other = await CreateAsync(manager, null, Unique("Otra"));
        var actor = Actor(manager);
        var published = await SeedFormAsync(actor, Unique("Orden de compra"));
        var draft = await SeedFormAsync(actor, Unique("Borrador"), published: false);
        var off = await SeedFormAsync(actor, Unique("Dada de baja"), deactivated: true);
        var gone = await SeedFormAsync(actor, Unique("Eliminado"), published: false);
        var added = await SeedFormAsync(actor, Unique("Remito"));
        var inGroup = await SeedFormAsync(actor, Unique("Pedido"));
        var catalog = await SeedCatalogAsync(Unique("Proveedores"));
        await FileAsync(manager, "definitions", published, folder.Id, other.Id);
        foreach (var form in new[] { draft, off, gone }) await FileAsync(manager, "definitions", form, folder.Id);
        await FileAsync(manager, "definitions", inGroup, group.Id);
        await FileAsync(manager, "catalogs", catalog, folder.Id);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Remove(await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == gone, Ct));
            await db.SaveChangesAsync(Ct);
        });

        using var read = await manager.GetAsync($"/api/forms/folders/{folder.Id}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore);
        await FormFolderContractCapture.WriteAsync(read, "forms", Ct);
        var current = FormIds(await read.ReadJsonAsync());
        Assert.Equal(new[] { published, draft, off }.Order(), current.Order());

        // The front keeps the deactivated form it read, which it does not list, and adds and takes out what was ticked.
        using var saved = await manager.PutAsJsonAsync($"/api/forms/folders/{folder.Id}/forms",
            new { expectedFormIds = current, formIds = new[] { off, added, draft } }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await FormFolderContractCapture.WriteAsync(saved, "forms-saved", Ct);
        Assert.Equal([off, added, draft], FormIds(await saved.ReadJsonAsync()));
        Assert.Equal(new[] { off, added, draft }.Order(), (await FormsOfAsync(manager, folder.Id)).Order());
        Assert.Equal([other.Id], await FoldersOfAsync(manager, "definitions", published));
        Assert.Equal([group.Id], await FoldersOfAsync(manager, "definitions", inGroup));
        Assert.Equal([folder.Id], await FoldersOfAsync(manager, "catalogs", catalog));
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormFolderMembership>()
            .AnyAsync(membership => membership.FolderId == folder.Id && membership.ItemId == gone, Ct)));

        using var stale = await manager.PutAsJsonAsync($"/api/forms/folders/{folder.Id}/forms",
            new { expectedFormIds = current, formIds = Array.Empty<Guid>() }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.Folder.SetChanged", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(3, (await FormsOfAsync(manager, folder.Id)).Length);
    }

    [Fact]
    public async Task A_missing_folder_one_below_the_second_level_and_a_missing_form_are_not_found()
    {
        using var manager = await OperatorAsync(P.Read, P.Manage);
        var top = await CreateAsync(manager, null, Unique("Depósitos"));
        var group = await CreateAsync(manager, top.Id, "Norte");
        // What the previous version may still write until the second deployment of FoldersAsMenus: a third level, which is not read. It
        // leaves the shared database when the test ends, also if it fails.
        var deep = BusinessFormFolder.Create(group.Id, "Nivel 3", null).Value;
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormFolder>().Add(deep);
            await db.SaveChangesAsync(Ct);
        });
        try
        {
            foreach (var id in new[] { Guid.NewGuid(), deep.Id })
            {
                Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync($"/api/forms/folders/{id}/forms", Ct)).StatusCode);
                using var put = await manager.PutAsJsonAsync($"/api/forms/folders/{id}/forms", new { expectedFormIds = Array.Empty<Guid>(), formIds = Array.Empty<Guid>() }, Ct);
                Assert.Equal("Forms.Folder.NotFound", (await put.ReadJsonAsync()).GetProperty("code").GetString());
            }
        }
        finally
        {
            await factory.ExecuteDbContextAsync(async db =>
            {
                db.Set<BusinessFormFolder>().RemoveRange(await db.Set<BusinessFormFolder>().Where(folder => folder.Id == deep.Id).ToListAsync(CancellationToken.None));
                await db.SaveChangesAsync(CancellationToken.None);
            });
        }
        // A thousand forms in each list pass the validator. The set read comes first: one that is not the current set is a conflict,
        // and forms that do not exist, saved against the current set, are not found.
        var thousand = Enumerable.Range(0, 1000).Select(_ => Guid.NewGuid()).ToArray();
        using var changed = await manager.PutAsJsonAsync($"/api/forms/folders/{group.Id}/forms",
            new { expectedFormIds = thousand, formIds = thousand }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("Forms.Folder.SetChanged", (await changed.ReadJsonAsync()).GetProperty("code").GetString());
        using var missing = await manager.PutAsJsonAsync($"/api/forms/folders/{group.Id}/forms",
            new { expectedFormIds = Array.Empty<Guid>(), formIds = thousand }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Forms.Definition.NotFound", (await missing.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Empty(await FormsOfAsync(manager, group.Id));
    }

    private static Guid[] FormIds(JsonElement json) => json.GetProperty("formIds").EnumerateArray().Select(id => id.GetGuid()).ToArray();

    private static async Task<Guid[]> FormsOfAsync(HttpClient client, Guid folderId)
    {
        using var response = await client.GetAsync($"/api/forms/folders/{folderId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return FormIds(await response.ReadJsonAsync());
    }
}
