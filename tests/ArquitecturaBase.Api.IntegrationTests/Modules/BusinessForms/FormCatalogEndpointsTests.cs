using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions ContractRequestSerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Manual_catalog_saves_metadata_and_delta_in_one_revision_and_stale_edit_leaves_no_partial_change()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var initial = new
        {
            name = new { es = "Proveedores", en = "Suppliers" },
            description = new { es = "Directorio de proveedores", en = "Supplier directory" },
            items = new object[]
            {
                Item("saved-a", null, "C-001", "Almacén Sur", null, 1, true),
                Item("saved-b", null, "C-002", "Ferretería Norte", "North hardware", 1, true),
            },
        };

        await ExportRequestBodyAsync(initial, "create-request");
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", initial, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await ExportBilingualResponseAsync(created, "created");
        var createdJson = await created.ReadJsonAsync();
        var catalog = createdJson.GetProperty("catalog");
        var catalogId = catalog.GetProperty("id").GetGuid();
        Assert.Equal(1, catalog.GetProperty("revision").GetInt64());
        Assert.Equal("Suppliers", catalog.GetProperty("name").GetProperty("en").GetString());
        var firstItemId = createdJson.GetProperty("itemIdentityMap").GetProperty("saved-a").GetGuid();
        var secondItemId = createdJson.GetProperty("itemIdentityMap").GetProperty("saved-b").GetGuid();

        var update = new
        {
            expectedRevision = 1,
            name = new { es = "Proveedores", en = "Suppliers" },
            description = new { es = "Directorio actualizado", en = "Updated supplier directory" },
            items = new object[]
            {
                Item("saved-a", firstItemId, "C-001A", "Almacén Sur", "South warehouse", 1, true),
                Item("new-local", null, "C-003", "Distribuidora Oeste", null, 3, true),
            },
        };

        await ExportRequestBodyAsync(update, "update-request");
        using var saved = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", update, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await ExportBilingualResponseAsync(saved, "updated");
        var savedJson = await saved.ReadJsonAsync();
        Assert.Equal(2, savedJson.GetProperty("catalog").GetProperty("revision").GetInt64());
        Assert.Equal(firstItemId, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>()
            .Where(item => item.Id == firstItemId).Select(item => item.Id).SingleAsync(Ct)));
        var newItemId = savedJson.GetProperty("itemIdentityMap").GetProperty("new-local").GetGuid();
        Assert.NotEqual(Guid.Empty, newItemId);

        using var detail = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await ExportBilingualResponseAsync(detail, "detail");
        Assert.Equal("Updated supplier directory", (await detail.ReadJsonAsync()).GetProperty("description").GetProperty("en").GetString());
        using var items = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=1&pageSize=20", Ct);
        Assert.Equal(HttpStatusCode.OK, items.StatusCode);
        await ExportBilingualResponseAsync(items, "items");
        var itemRows = (await items.ReadJsonAsync()).GetProperty("items");
        Assert.Equal(3, itemRows.GetArrayLength());
        var tieSortedIds = new[] { firstItemId, secondItemId }.Order().ToArray();
        Assert.Equal(tieSortedIds[0], itemRows[0].GetProperty("id").GetGuid());
        Assert.Equal(tieSortedIds[1], itemRows[1].GetProperty("id").GetGuid());
        var updatedFirst = Assert.Single(itemRows.EnumerateArray(), item => item.GetProperty("id").GetGuid() == firstItemId);
        Assert.Equal("C-001A", updatedFirst.GetProperty("code").GetString());
        Assert.Equal("South warehouse", updatedFirst.GetProperty("text").GetProperty("en").GetString());
        Assert.Contains(itemRows.EnumerateArray(), item => item.GetProperty("id").GetGuid() == secondItemId);

        using var firstPage = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=1&pageSize=1&sort=position", Ct);
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Equal(3, (await firstPage.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var invalidPage = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=0", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
        Assert.True((await invalidPage.ReadJsonAsync()).GetProperty("errors").TryGetProperty("page", out _));
        using var invalidSort = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?sort=missing", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);
        Assert.True((await invalidSort.ReadJsonAsync()).GetProperty("errors").TryGetProperty("sort", out _));

        var stale = new
        {
            expectedRevision = 1,
            name = new { es = "No guardar", en = "Do not save" },
            description = new { es = "No guardar", en = "Do not save" },
            items = new[] { Item("saved-a", firstItemId, "PARTIAL", "Cambio parcial", null, 1, true) },
        };
        await ExportRequestBodyAsync(stale, "conflict-request");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        using var conflict = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", stale, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await ExportBilingualResponseAsync(conflict, "conflict-es");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var conflictEnglish = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", stale, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflictEnglish.StatusCode);
        await ExportBilingualResponseAsync(conflictEnglish, "conflict-en");
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        using var unchanged = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        var unchangedJson = await unchanged.ReadJsonAsync();
        var unchangedCatalog = unchangedJson.GetProperty("name");
        Assert.Equal("Proveedores", unchangedCatalog.GetProperty("es").GetString());
        Assert.Equal(2, unchangedJson.GetProperty("revision").GetInt64());
        Assert.Equal("C-001A", await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>()
            .Where(item => item.Id == firstItemId).Select(item => item.Code).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Catalog_routes_require_read_and_manage_permissions_separately()
    {
        using var anonymous = factory.CreateClient();
        using var anonymousRead = await anonymous.GetAsync("/api/forms/catalogs/" + Guid.NewGuid(), Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRead.StatusCode);

        using var readOnly = await OperatorAsync(BusinessFormsPermissions.CatalogsRead);
        using var deniedWrite = await readOnly.PostAsJsonAsync("/api/forms/catalogs", Payload(), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);

        using var manageOnly = await OperatorAsync(BusinessFormsPermissions.CatalogsManage);
        using var deniedRead = await manageOnly.GetAsync("/api/forms/catalogs/" + Guid.NewGuid(), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
    }

    [Theory]
    [InlineData("es", "Proveedores", "Directorio de proveedores")]
    [InlineData("en", "Suppliers", "Supplier directory")]
    public async Task Catalog_list_uses_the_ui_culture_for_the_localized_columns(string culture, string expectedName, string expectedDescription)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        expectedName += " " + unique;
        expectedDescription += " " + unique;
        using var writer = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var payload = new
        {
            name = new { es = "Proveedores " + unique, en = "Suppliers " + unique },
            description = new { es = "Directorio de proveedores " + unique, en = "Supplier directory " + unique },
            items = Array.Empty<object>(),
        };
        using var created = await writer.PostAsJsonAsync("/api/forms/catalogs", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        foreach (var (nameEs, nameEn) in new[] { ("Almacenes " + unique, "Accounts " + unique), ("Zonales " + unique, "Warehouses " + unique) })
        {
            using var adjacent = await writer.PostAsJsonAsync("/api/forms/catalogs", new
            {
                name = new { es = nameEs, en = nameEn },
                description = new { es = "Prueba " + unique, en = "Test " + unique },
                items = Array.Empty<object>(),
            }, Ct);
            Assert.Equal(HttpStatusCode.Created, adjacent.StatusCode);
        }

        using var reader = await OperatorAsync(BusinessFormsPermissions.CatalogsRead);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var listed = await reader.GetAsync("/api/forms/catalogs?search=" + Uri.EscapeDataString(unique) + "&page=2&pageSize=1&sort=name", Ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        await ExportBilingualResponseAsync(listed, "list-" + culture);
        var list = await listed.ReadJsonAsync();
        Assert.Equal(2, list.GetProperty("page").GetInt32());
        Assert.Equal(1, list.GetProperty("pageSize").GetInt32());
        Assert.Equal(3, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, list.GetProperty("totalPages").GetInt32());
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(expectedName, row.GetProperty("name").GetString());
        Assert.Equal(expectedDescription, row.GetProperty("description").GetString());
        Assert.Equal("Manual", row.GetProperty("origin").GetString());
    }

    [Fact]
    public async Task Inactive_item_in_initial_catalog_save_stays_inactive_after_reload()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var payload = new
        {
            name = new { es = "Opciones inactivas", en = "Inactive options" },
            description = new { es = "Prueba", en = "Test" },
            items = new[] { Item("inactive-row", null, "OLD", "Opción antigua", null, 1, false) },
        };
        await ExportRequestBodyAsync(payload, "inactive-request");
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", payload, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdJson = await created.ReadJsonAsync();
        var catalogId = createdJson.GetProperty("catalog").GetProperty("id").GetGuid();

        using var reloaded = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?isActive=false", Ct);
        Assert.Equal(HttpStatusCode.OK, reloaded.StatusCode);
        var row = Assert.Single((await reloaded.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.False(row.GetProperty("isActive").GetBoolean());
        Assert.Equal(createdJson.GetProperty("itemIdentityMap").GetProperty("inactive-row").GetGuid(), row.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Missing_expected_revision_is_a_field_error_before_any_catalog_change()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", Payload(), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var missingRevision = new
        {
            name = new { es = "No guardar", en = "Do not save" },
            description = new { es = "Cambio", en = "Change" },
            items = new[] { Item("new-row", null, "NEW", "Nueva", null, 1, true) },
        };

        await ExportRequestBodyAsync(missingRevision, "missing-revision-request");
        using var rejected = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", missingRevision, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.True((await rejected.ReadJsonAsync()).GetProperty("errors").TryGetProperty("expectedRevision", out _));
        using var detail = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        var detailJson = await detail.ReadJsonAsync();
        Assert.Equal(1, detailJson.GetProperty("revision").GetInt64());
        Assert.Equal("Catálogo", detailJson.GetProperty("name").GetProperty("es").GetString());
        using var rows = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items", Ct);
        Assert.Equal(0, (await rows.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }

    private async Task<HttpClient> OperatorAsync(params string[] permissions)
    {
        var actor = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var roleName = "catalogs-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(roleName, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-operator")).Value, null, false, null, "es", Ct);
            await users.SetRolesAsync(user.Id, [roleName], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return client;
    }

    private static object Payload() => new
    {
        name = new { es = "Catálogo", en = "Catalog" },
        description = new { es = "Descripción", en = "Description" },
        items = Array.Empty<object>(),
    };

    private static async Task ExportBilingualResponseAsync(HttpResponseMessage response, string name)
    {
        var output = Environment.GetEnvironmentVariable("FORMS_CATALOG_HTTP_CONTRACT_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, "manual-bilingual");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), await response.Content.ReadAsByteArrayAsync(Ct), Ct);
    }

    private static async Task ExportRequestBodyAsync(object requestBody, string name)
    {
        var output = Environment.GetEnvironmentVariable("FORMS_CATALOG_HTTP_CONTRACT_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, "manual-bilingual");
        Directory.CreateDirectory(directory);
        var body = JsonSerializer.SerializeToUtf8Bytes(requestBody, ContractRequestSerializerOptions);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".request.json"), body, Ct);
    }

    private static object Item(string localId, Guid? itemId, string code, string textEs, string? textEn, int position, bool active) => new
    {
        localId,
        itemId,
        code,
        text = new { es = textEs, en = textEn },
        detail = new { es = (string?)null, en = (string?)null },
        position,
        isActive = active,
    };
}
