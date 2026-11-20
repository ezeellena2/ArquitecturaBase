using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;
using P = ArquitecturaBase.Domain.Modules.BusinessForms.BusinessFormsPermissions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// FoldersAsMenus (CM1 to CM6) on stored rows, the first of two deployments: under the folders lock, what was below the second level goes
// to its ancestor of the second level without repeating an item, that ancestor takes the first system key it lacked and every folder of
// the first level ends with an icon; a subfolder keeps the one the previous version shows. MenuPlacement and its CHECK stay for the
// previous version, with a default for this one, the CHECK that tied the icon to the own menu goes away, and going back gives the
// previous version its menus as MenuPlacement says. Until the next migration folds it again, this version reads and writes what the
// previous one writes as two levels.
[Collection(ApiTestGroup.Name)]
public sealed class FormFolderMenuMigrationTests(ApiFactory factory)
{
    private const string Before = "20261116224600_AddIdentityRetentionIndexes";
    private const string FoldersAsMenus = "20261120153647_FoldersAsMenus";
    private static readonly DateTime CreatedAt = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Ids in the order they were created, as Guid v7 keeps them: the third level is older than the fourth.
    private static readonly Guid Archive = Id(1), Deliveries = Id(2), Old = Id(3), North = Id(4), VeryOld = Id(5), Ancient = Id(6), Late = Id(7),
        Purchases = Id(8);
    private static readonly Guid Invoice = Id(101), Receipt = Id(102), Route = Id(103), Taxes = Id(104);

    [Fact]
    public async Task The_levels_below_the_second_go_to_their_ancestor_without_repeating_and_every_menu_gets_an_icon()
    {
        await using var database = Database("foldermenus");
        await using var scope = database.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrator = db.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(Before, Ct);
            // Archivo (outside the menu) > Viejos (its own menu, leaf) > Muy viejos (key test.keep) > Antiguos (key test.later); Repartos
            // (own menu, truck) > Norte (in a panel, key test.own) > Tarde (key test.lost).
            await FolderAsync(db, Archive, null, "Archivo", null, "None", null);
            await FolderAsync(db, Deliveries, null, "Repartos", null, "Own", "truck");
            await FolderAsync(db, Old, Archive, "Viejos", null, "Own", "leaf");
            await FolderAsync(db, North, Deliveries, "Norte", "test.own", "Forms", null);
            await FolderAsync(db, VeryOld, Old, "Muy viejos", "test.keep", "None", null);
            await FolderAsync(db, Ancient, VeryOld, "Antiguos", "test.later", "None", null);
            await FolderAsync(db, Late, North, "Tarde", "test.lost", "None", null);
            await MembershipAsync(db, Archive, "Form", Invoice);
            await MembershipAsync(db, Old, "Form", Receipt);
            await MembershipAsync(db, VeryOld, "Form", Invoice);
            await MembershipAsync(db, VeryOld, "Catalog", Taxes);
            await MembershipAsync(db, Ancient, "Form", Invoice);
            await MembershipAsync(db, Ancient, "Form", Receipt);
            await MembershipAsync(db, Late, "Form", Route);

            await migrator.MigrateAsync(cancellationToken: Ct);

            Assert.False(db.Database.HasPendingModelChanges());
            var folders = await db.Set<BusinessFormFolder>().AsNoTracking().OrderBy(folder => folder.Id).ToListAsync(Ct);
            Assert.Equal([Archive, Deliveries, Old, North], folders.Select(folder => folder.Id));
            Assert.Equal(["folder", "truck", "leaf", null], folders.Select(folder => folder.MenuIcon));
            Assert.Equal([null, null, "test.keep", "test.own"], folders.Select(folder => folder.SystemKey));
            var memberships = await db.Set<BusinessFormFolderMembership>().AsNoTracking().ToListAsync(Ct);
            Assert.Equal(new[] { (Archive, Invoice), (Old, Invoice), (Old, Receipt), (Old, Taxes), (North, Route) }.Order(),
                memberships.Select(value => (value.FolderId, value.ItemId)).Order());
            Assert.Equal(FormFolderItemKind.Catalog, memberships.Single(value => value.ItemId == Taxes).ItemKind);
            // The previous version still reads and writes MenuPlacement, untouched; this one inserts without it and takes the default.
            Assert.Equal(["None", "Own", "Own", "Forms"], await PlacementsAsync(db));
            Assert.Equal("'None'::character varying", await PlacementDefaultAsync(db));
            Assert.Equal(["CK_FormFolders_MenuPlacement"], await ChecksAsync(db));
            await CurrentFolderAsync(db, Purchases, null, "Compras", "package");
            Assert.Equal(["None", "Own", "Own", "Forms", "None"], await PlacementsAsync(db));

            await migrator.MigrateAsync(Before, Ct);

            // MenuPlacement says again what is a menu: only its own menus keep an icon, or take the folder, and the old CHECK is back.
            Assert.Equal(["None", "Own", "Own", "Forms", "None"], await PlacementsAsync(db));
            Assert.Equal([null, "truck", "leaf", null, null], await IconsAsync(db.Set<BusinessFormFolder>()));
            Assert.Equal(["CK_FormFolders_MenuIcon", "CK_FormFolders_MenuPlacement"], await ChecksAsync(db));
            Assert.Equal("", await PlacementDefaultAsync(db));

            await migrator.MigrateAsync(cancellationToken: Ct);

            Assert.Equal(["folder", "truck", "leaf", null, "folder"], await IconsAsync(db.Set<BusinessFormFolder>()));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    // The previous version writes folders under the folders lock: the migration takes the same lock before anything else, inside its own
    // transaction, so what that version writes while the bundle runs waits for the commit.
    [Fact]
    public async Task The_migration_takes_the_folders_lock_first_inside_its_own_transaction()
    {
        using var scope = factory.Services.CreateScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var migrations = db.Database.GetMigrations().ToList();
        var previous = migrations[migrations.IndexOf(FoldersAsMenus) - 1];

        var lines = db.GetService<IMigrator>().GenerateScript(previous, FoldersAsMenus).ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var key = await db.Database.SqlQuery<long>($"SELECT hashtextextended({FormAdvisoryLockKeys.Folders}, 0) AS \"Value\"").SingleAsync(Ct);
        Assert.Equal(Infrastructure.Persistence.Migrations.FoldersAsMenus.FoldersLockKey, key);
        Assert.Equal(["START TRANSACTION;", $"SELECT pg_advisory_xact_lock({key});"], lines[..2]);
        Assert.Equal("COMMIT;", lines[^1]);
        Assert.Single(lines, line => line == "START TRANSACTION;");
    }

    // Between the two deployments, or back on the previous image, the previous version may write a menu without an icon, a subfolder
    // with one and folders below the second level.
    [Fact]
    public async Task The_tree_and_the_menu_read_what_the_previous_version_writes_as_two_levels()
    {
        await using var database = Database("foldertolerance");
        await using var scope = database.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        try
        {
            await db.Database.MigrateAsync(Ct);
            // As the previous version writes them, with MenuPlacement: a menu without an icon, its subfolder as its own menu with one and
            // a third level with a catalog. As this version writes them, without MenuPlacement, which takes its default.
            await FolderAsync(db, Archive, null, "Archivo", null, "None", null);
            await CurrentFolderAsync(db, Deliveries, null, "Repartos", "folder");
            await FolderAsync(db, Old, Archive, "Viejos", null, "Own", "truck");
            await CurrentFolderAsync(db, North, Deliveries, "Norte", null);
            await FolderAsync(db, VeryOld, North, "Muy viejos", null, "None", null);
            Assert.Equal(["None", "None", "Own", "None", "None"], await PlacementsAsync(db));
            var catalog = BusinessFormCatalog.CreateManual("es", new Dictionary<string, string> { ["es"] = "Rubros" }, new Dictionary<string, string>());
            db.Set<BusinessFormCatalog>().Add(catalog);
            await db.SaveChangesAsync(Ct);
            await MembershipAsync(db, VeryOld, "Catalog", catalog.Id);
            using var client = await AdministratorAsync(database, scope.ServiceProvider);

            using var treeResponse = await client.GetAsync("/api/forms/folders?kind=forms", Ct);
            Assert.Equal(HttpStatusCode.OK, treeResponse.StatusCode);
            var tree = (await treeResponse.ReadJsonAsync()).GetProperty("folders").EnumerateArray().ToDictionary(folder => folder.GetProperty("id").GetGuid());
            Assert.Equal(new[] { Archive, Deliveries, Old, North }.Order(), tree.Keys.Order());
            Assert.Equal(("folder", "folder"), (tree[Archive].GetProperty("icon").GetString(), tree[Deliveries].GetProperty("icon").GetString()));
            Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (tree[Old].GetProperty("icon").ValueKind, tree[North].GetProperty("icon").ValueKind));

            using var menuResponse = await client.GetAsync("/api/forms/menu", Ct);
            Assert.Equal(HttpStatusCode.OK, menuResponse.StatusCode);
            var menus = (await menuResponse.ReadJsonAsync()).GetProperty("menus").EnumerateArray().ToArray();
            Assert.Equal([Archive, Deliveries], menus.Select(menu => menu.GetProperty("folderId").GetGuid()));
            Assert.All(menus, menu => Assert.Equal("folder", menu.GetProperty("icon").GetString()));
            Assert.Equal([Old, North], menus.Select(menu => Assert.Single(menu.GetProperty("groups").EnumerateArray()).GetProperty("folderId").GetGuid()));
            Assert.All(menus, menu => Assert.False(menu.GetProperty("groups")[0].TryGetProperty("icon", out _)));
            Assert.Equal([0, 0], menus.Select(menu => menu.GetProperty("catalogCount").GetInt32()));

            // The writes see the same two levels. A new menu takes the first icon no menu shows, and the icon the subfolder hides does not
            // count. Saving what a folder shows changes nothing, not even the revision.
            using var created = await client.PostAsJsonAsync("/api/forms/folders", new { name = "Compras" }, Ct);
            Assert.Equal("truck", (await created.ReadJsonAsync()).GetProperty("icon").GetString());
            Assert.Equal(("folder", 1L), await SaveAsync(client, Archive, new { expectedRevision = 1, name = "Archivo" }));
            Assert.Equal((null, 1L), await SaveAsync(client, Old, new { expectedRevision = 1, name = "Viejos", parentId = Archive }));
            Assert.Equal([null, "truck"], await IconsAsync(db.Set<BusinessFormFolder>().Where(folder => folder.Id == Archive || folder.Id == Old)));
            // The subfolder that goes up takes the first free icon, not the one it hid.
            Assert.Equal(("package", 2L), await SaveAsync(client, Old, new { expectedRevision = 1, name = "Viejos" }));

            // A menu and a group with a hidden third level below are renamed and change their icon as any other folder.
            Assert.Equal((null, 2L), await SaveAsync(client, North, new { expectedRevision = 1, name = "Zona norte", parentId = Deliveries }));
            Assert.Equal(("chart", 2L), await SaveAsync(client, Deliveries, new { expectedRevision = 1, name = "Entregas", icon = "chart" }));

            // The hidden folder is not found; counting what a delete takes counts it, with what it holds, since the delete takes it too.
            using var hiddenSave = await client.PutAsJsonAsync($"/api/forms/folders/{VeryOld}", new { expectedRevision = 1, name = "Muy viejos", parentId = North }, Ct);
            Assert.Equal((HttpStatusCode.NotFound, "Forms.Folder.NotFound"), (hiddenSave.StatusCode, (await hiddenSave.ReadJsonAsync()).GetProperty("code").GetString()));
            using var hiddenDelete = await DeleteAsync(client, VeryOld, 1);
            Assert.Equal(HttpStatusCode.NotFound, hiddenDelete.StatusCode);
            Assert.Equal((1, 0, 1), await DeletionAsync(client, North));
            Assert.Equal((2, 0, 1), await DeletionAsync(client, Deliveries));
            using var deleted = await DeleteAsync(client, North, 2);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            Assert.False(await db.Set<BusinessFormFolder>().AsNoTracking().AnyAsync(folder => folder.Id == VeryOld, Ct));
            Assert.False(await db.Set<BusinessFormFolderMembership>().AsNoTracking().AnyAsync(membership => membership.FolderId == VeryOld, Ct));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    private static async Task<(string? Icon, long Revision)> SaveAsync(HttpClient client, Guid id, object request)
    {
        using var response = await client.PutAsJsonAsync($"/api/forms/folders/{id}", request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = await response.ReadJsonAsync();
        return (saved.GetProperty("icon").GetString(), saved.GetProperty("revision").GetInt64());
    }

    private static async Task<(int Subfolders, int Forms, int Catalogs)> DeletionAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"/api/forms/folders/{id}/deletion", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.ReadJsonAsync();
        return (counts.GetProperty("subfolderCount").GetInt32(), counts.GetProperty("formCount").GetInt32(), counts.GetProperty("catalogCount").GetInt32());
    }

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, Guid id, long expectedRevision) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/forms/folders/{id}") { Content = JsonContent.Create(new { expectedRevision }) }, Ct);

    private static Task<List<string?>> IconsAsync(IQueryable<BusinessFormFolder> folders) =>
        folders.AsNoTracking().OrderBy(folder => folder.Id).Select(folder => folder.MenuIcon).ToListAsync(Ct);

    private WebApplicationFactory<Program> Database(string prefix) => factory.WithWebHostBuilder(builder => builder.UseSetting(
        $"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString(prefix)));

    private static Guid Id(int number) => Guid.Parse($"0195f3a0-0000-7000-8000-{number:D12}");

    private static Task<int> FolderAsync(ApplicationDbContext db, Guid id, Guid? parentId, string name, string? systemKey, string placement, string? icon) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormFolders" ("Id", "ParentId", "Name", "NormalizedName", "SystemKey", "MenuPlacement", "MenuIcon", "Revision", "CreatedAtUtc")
            VALUES ({id}, {parentId}, {name}, {name.ToUpperInvariant()}, {systemKey}, {placement}, {icon}, 1, {CreatedAt})
            """, Ct);

    // As this version writes a folder: without MenuPlacement, which it no longer maps.
    private static Task<int> CurrentFolderAsync(ApplicationDbContext db, Guid id, Guid? parentId, string name, string? icon) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormFolders" ("Id", "ParentId", "Name", "NormalizedName", "MenuIcon", "Revision", "CreatedAtUtc")
            VALUES ({id}, {parentId}, {name}, {name.ToUpperInvariant()}, {icon}, 1, {CreatedAt})
            """, Ct);

    private static Task<int> MembershipAsync(ApplicationDbContext db, Guid folderId, string kind, Guid itemId) =>
        db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormFolderMemberships" ("Id", "FolderId", "ItemKind", "ItemId") VALUES ({Guid.CreateVersion7()}, {folderId}, {kind}, {itemId})
            """, Ct);

    private static Task<List<string>> PlacementsAsync(ApplicationDbContext db) =>
        db.Database.SqlQuery<string>($"""SELECT "MenuPlacement" AS "Value" FROM "FormFolders" ORDER BY "Id" """).ToListAsync(Ct);

    private static Task<string> PlacementDefaultAsync(ApplicationDbContext db) => db.Database.SqlQuery<string>($"""
        SELECT COALESCE(column_default, '') AS "Value" FROM information_schema.columns
        WHERE table_name = 'FormFolders' AND column_name = 'MenuPlacement'
        """).SingleAsync(Ct);

    private static Task<List<string>> ChecksAsync(ApplicationDbContext db) => db.Database.SqlQuery<string>($"""
        SELECT conname::text AS "Value" FROM pg_constraint WHERE conrelid = '"FormFolders"'::regclass AND contype = 'c' ORDER BY conname
        """).ToListAsync(Ct);

    // An administrator of forms who also sees responses, for the tree of «Formularios». The migrated database has no seed: creating a
    // user assigns the initial role.
    private static async Task<HttpClient> AdministratorAsync(WebApplicationFactory<Program> database, IServiceProvider services)
    {
        var role = "folder-tolerance-" + Guid.NewGuid().ToString("N");
        var created = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await roles.CreateAsync(SystemRoles.User, null, [], token);
            await roles.CreateAsync(role, null, [P.Manage, P.RecordsRead], token);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("folder-tolerance")).Value, null, false, null, "es", token);
            await users.SetRolesAsync(user.Id, [role], token);
            return Result.Success(user.Id);
        }, CommitPolicy.OnSuccess, Ct);
        var client = database.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, created.Value.ToString());
        return client;
    }
}
