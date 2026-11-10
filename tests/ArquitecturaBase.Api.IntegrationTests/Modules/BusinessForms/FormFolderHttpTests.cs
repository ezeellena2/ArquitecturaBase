using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// CP1-A to CP5-A with PostgreSQL: the tree pruned per listing, deleting with subfolders without touching items, the folders lock
// against cycles, the 409s, the system folder and the menu. Folders are shared by the whole database, so every assertion looks only at
// this test's own folders and items.
[Collection(ApiTestGroup.Name)]
public sealed partial class FormFolderHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Each_route_needs_a_session_and_its_own_permission()
    {
        using var anonymous = factory.CreateClient();
        using var nothing = await OperatorAsync();
        using var designs = await OperatorAsync(P.Read);
        using var catalogs = await OperatorAsync(P.CatalogsRead);
        using var manager = await OperatorAsync(P.Read, P.Manage);
        var folder = await CreateAsync(manager, null, Unique("Permisos"));
        var form = await SeedFormAsync(Actor(manager), Unique("Permiso"));

        foreach (var path in new[] { "/api/forms/folders?kind=designs", "/api/forms/menu", $"/api/forms/definitions/{form}/folders" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nothing.GetAsync("/api/forms/folders?kind=designs", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await catalogs.GetAsync("/api/forms/folders?kind=designs", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await designs.GetAsync("/api/forms/folders?kind=forms", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await designs.GetAsync("/api/forms/folders?kind=menu", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await designs.PostAsJsonAsync("/api/forms/folders", new { name = Unique("No") }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await designs.PutAsJsonAsync($"/api/forms/folders/{folder.Id}/menu",
            new { expectedRevision = 1, placement = "Own" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await designs.PutAsJsonAsync($"/api/forms/definitions/{form}/folders",
            new { expectedFolderIds = Array.Empty<Guid>(), folderIds = new[] { folder.Id } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync($"/api/forms/catalogs/{Guid.NewGuid()}/folders", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await designs.GetAsync($"/api/forms/definitions/{Guid.NewGuid()}/folders", Ct)).StatusCode);

        using var menu = await nothing.GetAsync("/api/forms/menu", Ct);
        Assert.Equal(HttpStatusCode.OK, menu.StatusCode);
        Assert.True(menu.Headers.CacheControl?.NoStore);
        var empty = await menu.ReadJsonAsync();
        Assert.Empty(empty.GetProperty("menus").EnumerateArray());
        Assert.Empty(empty.GetProperty("forms").EnumerateArray());
        Assert.Empty(empty.GetProperty("administration").EnumerateArray());
    }

    [Fact]
    public async Task The_tree_of_each_listing_shows_only_folders_with_something_visible_and_everything_to_whoever_administers()
    {
        using var manager = await OperatorAsync(P.Read, P.Manage, P.CatalogsRead, P.CatalogsManage);
        using var designs = await OperatorAsync(P.Read);
        using var catalogs = await OperatorAsync(P.CatalogsRead);
        using var forms = await OperatorAsync(P.RecordsCreate);
        var top = await CreateAsync(manager, null, Unique("Facturación"));
        var child = await CreateAsync(manager, top.Id, "Notas");
        var published = await CreateAsync(manager, null, Unique("Repartos"));
        var empty = await CreateAsync(manager, null, Unique("Vacía"));
        var deleted = await CreateAsync(manager, null, Unique("Borrados"));
        var actor = Actor(manager);
        await FileAsync(manager, "definitions", await SeedFormAsync(actor, Unique("Borrador"), published: false), child.Id);
        await FileAsync(manager, "catalogs", await SeedCatalogAsync(Unique("Rubros")), child.Id);
        await FileAsync(manager, "definitions", await SeedFormAsync(actor, Unique("Hoja de ruta")), published.Id);
        var gone = await SeedFormAsync(actor, Unique("Eliminado"), published: false);
        await FileAsync(manager, "definitions", gone, deleted.Id);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Remove(await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == gone, Ct));
            await db.SaveChangesAsync(Ct);
        });
        Guid[] mine = [top.Id, child.Id, published.Id, empty.Id, deleted.Id];

        async Task<Guid[]> TreeAsync(HttpClient client, string kind)
        {
            using var response = await client.GetAsync("/api/forms/folders?kind=" + kind, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var json = await response.ReadJsonAsync();
            Assert.Equal(client == manager, json.GetProperty("canManage").GetBoolean());
            return json.GetProperty("folders").EnumerateArray().Select(folder => folder.GetProperty("id").GetGuid()).Where(mine.Contains).ToArray();
        }

        Assert.Equal(new[] { top.Id, child.Id, published.Id }.Order(), (await TreeAsync(designs, "designs")).Order());
        Assert.Equal(new[] { top.Id, child.Id }.Order(), (await TreeAsync(catalogs, "catalogs")).Order());
        Assert.Equal([published.Id], await TreeAsync(forms, "forms"));
        Assert.Equal(mine.Order(), (await TreeAsync(manager, "designs")).Order());
    }

    [Fact]
    public async Task Deleting_a_folder_takes_its_subfolders_and_leaves_every_item_in_its_other_folders_or_without_one()
    {
        using var manager = await OperatorAsync(P.Read, P.Manage, P.CatalogsRead, P.CatalogsManage);
        var top = await CreateAsync(manager, null, Unique("Archivo"));
        var child = await CreateAsync(manager, top.Id, "Viejos");
        var grandchild = await CreateAsync(manager, child.Id, "Muy viejos");
        var other = await CreateAsync(manager, null, Unique("Vigentes"));
        var actor = Actor(manager);
        var kept = await SeedFormAsync(actor, Unique("Remito"));
        var lonely = await SeedFormAsync(actor, Unique("Recibo"));
        var catalog = await SeedCatalogAsync(Unique("Depósitos"));
        await FileAsync(manager, "definitions", kept, grandchild.Id, other.Id);
        await FileAsync(manager, "definitions", lonely, top.Id, child.Id);
        await FileAsync(manager, "catalogs", catalog, child.Id);

        using var preview = await manager.GetAsync($"/api/forms/folders/{top.Id}/deletion", Ct);
        var impact = await preview.ReadJsonAsync();
        Assert.Equal((1L, 2, 2, 1), (impact.GetProperty("revision").GetInt64(), impact.GetProperty("subfolderCount").GetInt32(),
            impact.GetProperty("formCount").GetInt32(), impact.GetProperty("catalogCount").GetInt32()));
        using var stale = await DeleteAsync(manager, top.Id, 2);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.Folder.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());

        using var deleted = await DeleteAsync(manager, top.Id, 1);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormFolder>().Where(folder =>
            folder.Id == top.Id || folder.Id == child.Id || folder.Id == grandchild.Id || folder.Id == other.Id).Select(folder => folder.Id).ToListAsync(Ct));
        Assert.Equal([other.Id], remaining);
        Assert.Equal([other.Id], await FoldersOfAsync(manager, "definitions", kept));
        Assert.Empty(await FoldersOfAsync(manager, "definitions", lonely));
        Assert.Empty(await FoldersOfAsync(manager, "catalogs", catalog));
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/forms/definitions/{lonely}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync($"/api/forms/catalogs/{catalog}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(manager, top.Id, 1)).StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_moves_that_would_form_a_cycle_let_only_one_through()
    {
        using var manager = await OperatorAsync(P.Manage);
        using var other = await ClientAsync(Actor(manager));
        var first = await CreateAsync(manager, null, Unique("Primera"));
        var second = await CreateAsync(manager, null, Unique("Segunda"));

        var moves = await Task.WhenAll(
            manager.PutAsJsonAsync($"/api/forms/folders/{first.Id}", new { expectedRevision = 1, name = first.Name, parentId = second.Id }, Ct),
            other.PutAsJsonAsync($"/api/forms/folders/{second.Id}", new { expectedRevision = 1, name = second.Name, parentId = first.Id }, Ct));

        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.BadRequest], moves.Select(move => move.StatusCode).Order());
        var refused = await moves.Single(move => move.StatusCode == HttpStatusCode.BadRequest).ReadJsonAsync();
        Assert.Equal("Forms.Folder.Cycle", refused.GetProperty("code").GetString());
        Assert.True(refused.GetProperty("errors").TryGetProperty("parentId", out _));
        var parents = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormFolder>().Where(folder => folder.Id == first.Id || folder.Id == second.Id)
            .ToDictionaryAsync(folder => folder.Id, folder => folder.ParentId, Ct));
        Assert.Single(parents.Values, parent => parent is null);
        foreach (var move in moves) move.Dispose();
    }

    [Fact]
    public async Task Names_are_unique_per_parent_without_case_and_the_fifth_level_is_the_last()
    {
        using var manager = await OperatorAsync(P.CatalogsManage);
        var name = Unique("Compras");
        var top = await CreateAsync(manager, null, name);

        using var repeated = await manager.PostAsJsonAsync("/api/forms/folders", new { name = " " + name.ToUpperInvariant() + " " }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, repeated.StatusCode);
        var error = await repeated.ReadJsonAsync();
        Assert.Equal("Forms.Folder.NameTaken", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("errors").TryGetProperty("name", out _));
        Assert.Equal(top.Id, (await CreateAsync(manager, top.Id, name)).ParentId);
        var deepest = top;
        for (var level = 2; level <= 5; level++) deepest = await CreateAsync(manager, deepest.Id, "Nivel " + level);
        using var tooDeep = await manager.PostAsJsonAsync("/api/forms/folders", new { parentId = deepest.Id, name = "Nivel 6" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooDeep.StatusCode);
        Assert.Equal("Forms.Folder.TooDeep", (await tooDeep.ReadJsonAsync()).GetProperty("code").GetString());
        using var missingParent = await manager.PostAsJsonAsync("/api/forms/folders", new { parentId = Guid.NewGuid(), name = "Huérfana" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missingParent.StatusCode);
        using var unknown = await manager.PostAsJsonAsync("/api/forms/folders", new { name = "Extra", color = "red" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Stale_revisions_and_changed_sets_are_conflicts_that_save_nothing()
    {
        using var manager = await OperatorAsync(P.Read, P.Manage);
        var folder = await CreateAsync(manager, null, Unique("Turnos"));
        var other = await CreateAsync(manager, null, Unique("Guardias"));
        var form = await SeedFormAsync(Actor(manager), Unique("Turno"));

        using var renamed = await manager.PutAsJsonAsync($"/api/forms/folders/{folder.Id}", new { expectedRevision = 1, name = folder.Name + " B" }, Ct);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(2, (await renamed.ReadJsonAsync()).GetProperty("revision").GetInt64());
        foreach (var stale in new[]
        {
            await manager.PutAsJsonAsync($"/api/forms/folders/{folder.Id}", new { expectedRevision = 1, name = "Otra" }, Ct),
            await manager.PutAsJsonAsync($"/api/forms/folders/{folder.Id}/menu", new { expectedRevision = 1, placement = "Own" }, Ct),
        })
        {
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            Assert.Equal("Forms.Folder.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        }
        using var menu = await manager.PutAsJsonAsync($"/api/forms/folders/{folder.Id}/menu", new { expectedRevision = 2, placement = "Own", icon = "truck" }, Ct);
        var placed = await menu.ReadJsonAsync();
        Assert.Equal(("Own", "truck", 3L), (placed.GetProperty("menuPlacement").GetString(), placed.GetProperty("menuIcon").GetString(), placed.GetProperty("revision").GetInt64()));
        using var icon = await manager.PutAsJsonAsync($"/api/forms/folders/{folder.Id}/menu", new { expectedRevision = 3, placement = "Administration", icon = "truck" }, Ct);
        Assert.Equal("Forms.Folder.IconInvalid", (await icon.ReadJsonAsync()).GetProperty("code").GetString());

        await FileAsync(manager, "definitions", form, folder.Id);
        using var changed = await manager.PutAsJsonAsync($"/api/forms/definitions/{form}/folders",
            new { expectedFolderIds = Array.Empty<Guid>(), folderIds = new[] { other.Id } }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        Assert.Equal("Forms.Folder.SetChanged", (await changed.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal([folder.Id], await FoldersOfAsync(manager, "definitions", form));
    }

    [Fact]
    public async Task The_system_folder_comes_back_even_after_a_rename_and_is_created_again_after_a_delete()
    {
        using var manager = await OperatorAsync(P.CatalogsRead, P.CatalogsManage);
        var key = "test." + Guid.NewGuid().ToString("N");
        var name = Unique("Sistema");
        var first = await SeedCatalogAsync(Unique("Alícuotas"));
        var second = await SeedCatalogAsync(Unique("Comprobantes"));
        var third = await SeedCatalogAsync(Unique("Monedas"));

        var folder = await SystemFileAsync(first, key, name);
        var row = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormFolder>().SingleAsync(value => value.Id == folder, Ct));
        Assert.Equal((name, key, (Guid?)null, FormFolderMenuPlacement.None), (row.Name, row.SystemKey, row.ParentId, row.MenuPlacement));
        Assert.True((await manager.PutAsJsonAsync($"/api/forms/folders/{folder}", new { expectedRevision = 1, name = name + " propia" }, Ct)).IsSuccessStatusCode);

        Assert.Equal(folder, await SystemFileAsync(second, key, name));
        Assert.Equal([folder], await FoldersOfAsync(manager, "catalogs", second));
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(manager, folder, 2)).StatusCode);

        var again = await SystemFileAsync(third, key, name);
        Assert.NotEqual(folder, again);
        Assert.Equal([again], await FoldersOfAsync(manager, "catalogs", third));
        Assert.Empty(await FoldersOfAsync(manager, "catalogs", first));
    }

    [Fact]
    public async Task The_menu_holds_only_active_forms_pruned_by_permission_with_the_subfolders_in_their_group()
    {
        using var manager = await OperatorAsync(P.Read, P.Manage, P.CatalogsRead, P.CatalogsManage);
        using var loader = await OperatorAsync(P.RecordsCreate);
        using var designer = await OperatorAsync(P.Read, P.Manage);
        loader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var own = await CreateAsync(manager, null, Unique("Repartos"));
        var north = await CreateAsync(manager, own.Id, "Norte");
        var late = await CreateAsync(manager, north.Id, "Tarde");
        var empty = await CreateAsync(manager, own.Id, "Vacía");
        var admin = await CreateAsync(manager, null, Unique("Facturación"));
        var panel = await CreateAsync(manager, null, Unique("Solo borradores"));
        await PlaceAsync(manager, own.Id, "Own", "truck");
        await PlaceAsync(manager, admin.Id, "Administration", null);
        await PlaceAsync(manager, panel.Id, "Forms", null);
        var actor = Actor(manager);
        var prefix = "M" + Guid.NewGuid().ToString("N")[..6];
        var route = await SeedFormAsync(actor, prefix + " Hoja de ruta");
        var delivery = await SeedFormAsync(actor, prefix + " Entrega");
        var load = await SeedFormAsync(actor, prefix + " Carga en ruta");
        var draft = await SeedFormAsync(actor, prefix + " Borrador", published: false);
        var off = await SeedFormAsync(actor, prefix + " Dada de baja", deactivated: true);
        await FileAsync(manager, "definitions", route, own.Id);
        await FileAsync(manager, "definitions", delivery, north.Id, admin.Id);
        await FileAsync(manager, "definitions", load, late.Id);
        await FileAsync(manager, "definitions", draft, own.Id, empty.Id, panel.Id);
        await FileAsync(manager, "definitions", off, own.Id, panel.Id);
        await FileAsync(manager, "catalogs", await SeedCatalogAsync(prefix + " Rubros"), own.Id, panel.Id);

        using var response = await loader.GetAsync("/api/forms/menu", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var menu = await response.ReadJsonAsync();

        var section = menu.GetProperty("menus").EnumerateArray().Single(item => item.GetProperty("folderId").GetGuid() == own.Id);
        Assert.Equal((own.Name, "truck"), (section.GetProperty("name").GetString(), section.GetProperty("icon").GetString()));
        Assert.Equal([route], Ids(section.GetProperty("forms")));
        var group = Assert.Single(section.GetProperty("groups").EnumerateArray());
        Assert.Equal((north.Id, "Norte"), (group.GetProperty("folderId").GetGuid(), group.GetProperty("name").GetString()));
        Assert.Equal([load, delivery], Ids(group.GetProperty("forms")));
        Assert.Equal(prefix + " Carga en ruta", group.GetProperty("forms")[0].GetProperty("name").GetString());
        var billing = menu.GetProperty("administration").EnumerateArray().Single(item => item.GetProperty("folderId").GetGuid() == admin.Id);
        Assert.Equal([delivery], Ids(billing.GetProperty("forms")));
        Assert.DoesNotContain(menu.GetProperty("forms").EnumerateArray(), item => item.GetProperty("folderId").GetGuid() == panel.Id);

        using var none = await designer.GetAsync("/api/forms/menu", Ct);
        var nothing = await none.ReadJsonAsync();
        Assert.DoesNotContain(nothing.GetProperty("menus").EnumerateArray(), item => item.GetProperty("folderId").GetGuid() == own.Id);
        Assert.Empty(nothing.GetProperty("administration").EnumerateArray());
    }

    private static Guid[] Ids(JsonElement forms) => forms.EnumerateArray().Select(form => form.GetProperty("id").GetGuid()).ToArray();

    private static string Unique(string name) => name + " " + Guid.NewGuid().ToString("N")[..8];

    private static Guid Actor(HttpClient client) => Guid.Parse(client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());

    private async Task<HttpClient> OperatorAsync(params string[] permissions)
    {
        var actor = await factory.InTransactionAsync(async services =>
        {
            var name = "folders-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(name, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("forms-folders")).Value, null, false, null, "es", Ct);
            await users.SetRolesAsync(user.Id, [name], Ct);
            return user.Id;
        });
        return await ClientAsync(actor);
    }

    private Task<HttpClient> ClientAsync(Guid actor)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return Task.FromResult(client);
    }

    private static async Task<FormFolderResponse> CreateAsync(HttpClient client, Guid? parentId, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/forms/folders", new { parentId, name }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<FormFolderResponse>(Json, Ct))!;
    }

    private static async Task PlaceAsync(HttpClient client, Guid id, string placement, string? icon)
    {
        using var response = await client.PutAsJsonAsync($"/api/forms/folders/{id}/menu", new { expectedRevision = 1, placement, icon }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid id, long expectedRevision) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/folders/{id}") { Content = JsonContent.Create(new { expectedRevision }) }, Ct);

    private static async Task FileAsync(HttpClient client, string items, Guid itemId, params Guid[] folderIds)
    {
        var current = await FoldersOfAsync(client, items, itemId);
        using var response = await client.PutAsJsonAsync($"/api/forms/{items}/{itemId}/folders", new { expectedFolderIds = current, folderIds }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(folderIds.Order(), (await response.ReadJsonAsync()).GetProperty("folderIds").EnumerateArray().Select(id => id.GetGuid()).Order());
    }

    private static async Task<Guid[]> FoldersOfAsync(HttpClient client, string items, Guid itemId)
    {
        using var response = await client.GetAsync($"/api/forms/{items}/{itemId}/folders", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.ReadJsonAsync()).GetProperty("folderIds").EnumerateArray().Select(id => id.GetGuid()).ToArray();
    }

    private async Task<Guid> SystemFileAsync(Guid catalogId, string key, string name)
    {
        using var scope = factory.Services.CreateScope();
        var linker = scope.ServiceProvider.GetRequiredService<FormSystemFolderLinker>();
        var filed = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(
            ct => linker.FileAsync(FormFolderItemKind.Catalog, catalogId, key, name, ct), CommitPolicy.OnSuccess, Ct);
        Assert.True(filed.IsSuccess);
        return filed.Value;
    }

    private async Task<Guid> SeedCatalogAsync(string name)
    {
        var texts = new Dictionary<string, string> { ["es"] = name };
        var catalog = BusinessFormCatalog.CreateManual("es", texts, new Dictionary<string, string>());
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormCatalog>().Add(catalog);
            await db.SaveChangesAsync(Ct);
        });
        return catalog.Id;
    }

    // A form with one question: published with its active version (and deactivated if asked), or only a draft.
    private async Task<Guid> SeedFormAsync(Guid actor, string name, bool published = true, bool deactivated = false)
    {
        var prefix = "F" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        var field = new CanonicalFormField(Guid.NewGuid(), "value", "longText", new Dictionary<string, string> { ["es"] = "Valor" }, null, false, 600, null);
        var names = new Dictionary<string, string> { ["es"] = name };
        var buttons = new Dictionary<string, string> { ["es"] = "Enviar" };
        var definition = BusinessFormDefinition.Create();
        Assert.True(definition.SetDraft(1, name, JsonSerializer.Serialize(names), prefix, "es", JsonSerializer.Serialize(buttons),
            JsonSerializer.Serialize(new FormQuestionSchema(1, [field]), Json), 1, true).IsSuccess);
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            if (!published) return;
            var version = BusinessFormVersion.Create(definition.Id, 1, "folder-test");
            version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, [field]), Json));
            version.SealPresentation(JsonSerializer.Serialize(names), JsonSerializer.Serialize(buttons), "es", 1);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, actor, ["es"]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess);
            await db.SaveChangesAsync(Ct);
            if (!deactivated) return;
            Assert.True(definition.Deactivate(definition.Revision, actor, factory.Clock.GetUtcNow().UtcDateTime).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        return definition.Id;
    }
}
