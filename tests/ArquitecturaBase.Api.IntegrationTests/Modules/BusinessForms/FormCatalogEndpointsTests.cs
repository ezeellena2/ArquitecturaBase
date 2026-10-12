using System.Net;
using System.Net.Http.Json;
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
                Item("saved-b", null, "C-002", "Ferretería Norte", "North hardware", 2, true),
            },
        };

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
        Assert.Equal("C-001A", itemRows[0].GetProperty("code").GetString());
        Assert.Equal("South warehouse", itemRows[0].GetProperty("text").GetProperty("en").GetString());
        Assert.Contains(itemRows.EnumerateArray(), item => item.GetProperty("id").GetGuid() == secondItemId);

        var stale = new
        {
            expectedRevision = 1,
            name = new { es = "No guardar", en = "Do not save" },
            description = new { es = "No guardar", en = "Do not save" },
            items = new[] { Item("saved-a", firstItemId, "PARTIAL", "Cambio parcial", null, 1, true) },
        };
        using var conflict = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", stale, Ct);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
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
