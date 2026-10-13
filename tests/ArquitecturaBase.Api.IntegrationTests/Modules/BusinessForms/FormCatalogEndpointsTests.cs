using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogEndpointsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions ContractRequestSerializerOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_status_is_saved_with_revision_reloads_and_requires_manage(bool filters)
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var name = "Status " + Guid.NewGuid().ToString("N");
        var texts = new { es = name, en = name };
        var definition = new { textColumn = "name", detailColumn = (string?)null, conditions = Array.Empty<object>(), excludeRecipient = false, sortColumn = "name", sortDescending = false };
        object body = filters ? new { name = texts, description = texts, filters = definition }
            : new { name = texts, description = texts, items = new[] { Item("first", null, "ONE", "Uno", null, true) } };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs" + (filters ? "/filters" : ""), body, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var path = $"/api/forms/catalogs/{id}" + (filters ? "/filters" : "");
        object Update(long revision, bool active) => filters
            ? new { expectedRevision = revision, name = texts, description = texts, filters = definition, isActive = active }
            : new { expectedRevision = revision, name = texts, description = texts, items = Array.Empty<object>(), isActive = active };
        using var reader = await OperatorAsync(BusinessFormsPermissions.CatalogsRead);
        using var forbidden = await reader.PutAsJsonAsync(path, Update(1, false), Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var inactive = await client.PutAsJsonAsync(path, Update(1, false), Ct);
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        Assert.False((await inactive.ReadJsonAsync()).GetProperty("catalog").GetProperty("isActive").GetBoolean());
        using var reload = await client.GetAsync($"/api/forms/catalogs/{id}", Ct);
        var detail = await reload.ReadJsonAsync();
        Assert.Equal(2, detail.GetProperty("revision").GetInt64());
        Assert.False(detail.GetProperty("isActive").GetBoolean());
        using var list = await client.GetAsync("/api/forms/catalogs?state=Inactive&search=" + name, Ct);
        var row = Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal("Inactive", row.GetProperty("state").GetString());
        using var stale = await client.PutAsJsonAsync(path, Update(1, true), Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var active = await client.PutAsJsonAsync(path, Update(2, true), Ct);
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.True((await active.ReadJsonAsync()).GetProperty("catalog").GetProperty("isActive").GetBoolean());
        Assert.Equal(filters ? 0 : 1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().CountAsync(item => item.CatalogId == id, Ct)));
    }

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
                Item("saved-a", null, "C-001", "Almacén Sur", null, true),
                Item("saved-b", null, "C-002", "Ferretería Norte", "North hardware", true),
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
                Item("saved-a", firstItemId, "C-001A", "Almacén Sur", "South warehouse", true),
                Item("new-local", null, "C-003", "Distribuidora Oeste", null, true),
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
        Assert.Equal(firstItemId, itemRows[0].GetProperty("id").GetGuid());
        Assert.Equal(secondItemId, itemRows[1].GetProperty("id").GetGuid());
        Assert.Equal(1, itemRows[0].GetProperty("position").GetInt32());
        Assert.Equal(2, itemRows[1].GetProperty("position").GetInt32());
        Assert.Equal(3, itemRows[2].GetProperty("position").GetInt32());
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
            items = new[] { Item("saved-a", firstItemId, "PARTIAL", "Cambio parcial", null, true) },
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
    public async Task Catalog_list_returns_origin_counts_for_the_full_filtered_result()
    {
        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var suffix = Guid.NewGuid().ToString("N");

        foreach (var kind in new[] { "Manual", "Manual", "Filters" })
        {
            var payload = new
            {
                name = new { es = "Origen " + suffix, en = "Origin " + suffix },
                description = new { es = "Conteo de origen", en = "Origin count" },
            };
            using var response = kind == "Manual"
                ? await manager.PostAsJsonAsync("/api/forms/catalogs", new { payload.name, payload.description, items = Array.Empty<object>() }, Ct)
                : await manager.PostAsJsonAsync("/api/forms/catalogs/filters", new
                {
                    payload.name,
                    payload.description,
                    filters = new { textColumn = "name", detailColumn = (string?)null, conditions = Array.Empty<object>(),
                        excludeRecipient = true, sortColumn = "name", sortDescending = false },
                }, Ct);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        using var page = await manager.GetAsync(
            "/api/forms/catalogs?search=" + Uri.EscapeDataString(suffix) + "&state=Active&kind=Manual&page=1&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var pageJson = await page.ReadJsonAsync();
        Assert.Equal(2, pageJson.GetProperty("totalCount").GetInt32());
        Assert.Single(pageJson.GetProperty("items").EnumerateArray());
        var originCounts = pageJson.GetProperty("filterCounts").GetProperty("byKind");
        Assert.Equal(3, originCounts.GetProperty("all").GetInt32());
        Assert.Equal(2, originCounts.GetProperty("manual").GetInt32());
        Assert.Equal(1, originCounts.GetProperty("filters").GetInt32());

        using var invalidState = await manager.GetAsync("/api/forms/catalogs?state=Missing", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidState.StatusCode);
        Assert.True((await invalidState.ReadJsonAsync()).GetProperty("errors").TryGetProperty("state", out _));
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

    [Fact]
    public async Task Catalog_usage_count_is_hidden_without_form_read_permission()
    {
        var unique = Guid.NewGuid().ToString("N");
        using var writer = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var created = await writer.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = unique, en = unique },
            description = new { es = "Prueba", en = "Test" },
            items = Array.Empty<object>(),
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdJson = await created.ReadJsonAsync();
        var catalogId = createdJson.GetProperty("catalog").GetProperty("id").GetGuid();
        Assert.Equal(JsonValueKind.Null, createdJson.GetProperty("catalog").GetProperty("usageCount").ValueKind);

        using var detail = await writer.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(JsonValueKind.Null, (await detail.ReadJsonAsync()).GetProperty("usageCount").ValueKind);
        using var listed = await writer.GetAsync("/api/forms/catalogs?search=" + Uri.EscapeDataString(unique), Ct);
        Assert.Equal(JsonValueKind.Null, (await listed.ReadJsonAsync()).GetProperty("items")[0].GetProperty("usageCount").ValueKind);

        using var formReader = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.Read);
        using var readable = await formReader.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(0, (await readable.ReadJsonAsync()).GetProperty("usageCount").GetInt32());
    }

    [Fact]
    public async Task English_item_search_and_sort_use_the_text_shown_when_translation_is_missing_empty_or_blank()
    {
        using var writer = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var created = await writer.PostAsJsonAsync("/api/forms/catalogs", Payload(), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var ids = await factory.InTransactionAsync(async services =>
        {
            var items = new[]
            {
                BusinessFormCatalogItem.Create(catalogId, "NULL", "Zebra", null, null, null, 1),
                BusinessFormCatalogItem.Create(catalogId, "EMPTY", "Alpha", "", null, null, 2),
                BusinessFormCatalogItem.Create(catalogId, "BLANK", "Middle", "   ", null, null, 3),
                BusinessFormCatalogItem.Create(catalogId, "TRANSLATED", "Beta", "Bravo", null, null, 4),
            };
            services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormCatalogItem>().AddRange(items);
            return items.Select(item => item.Id).ToArray();
        });
        using var reader = await OperatorAsync(BusinessFormsPermissions.CatalogsRead);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");

        foreach (var (search, expectedId) in new[] { ("Zebra", ids[0]), ("Alpha", ids[1]), ("Middle", ids[2]) })
        {
            using var response = await reader.GetAsync($"/api/forms/catalogs/{catalogId}/items?search={Uri.EscapeDataString(search)}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(expectedId, (await response.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        }

        using var sorted = await reader.GetAsync($"/api/forms/catalogs/{catalogId}/items?sort=text&pageSize=20", Ct);
        Assert.Equal(HttpStatusCode.OK, sorted.StatusCode);
        var rows = (await sorted.ReadJsonAsync()).GetProperty("items");
        Assert.Equal(new[] { ids[1], ids[3], ids[2], ids[0] }, rows.EnumerateArray()
            .Select(row => row.GetProperty("id").GetGuid()));
        Assert.Equal("", rows[0].GetProperty("text").GetProperty("en").GetString());
        Assert.Equal("   ", rows[2].GetProperty("text").GetProperty("en").GetString());
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
            items = new[] { Item("inactive-row", null, "OLD", "Opción antigua", null, false) },
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
            items = new[] { Item("new-row", null, "NEW", "Nueva", null, true) },
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

    [Fact]
    public async Task Manual_catalog_contract_rejects_client_supplied_positions()
    {
        var unique = Guid.NewGuid().ToString("N");
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var duplicatePositions = new
        {
            name = new { es = "Posiciones " + unique, en = "Positions " + unique },
            description = new { es = "Prueba", en = "Test" },
            items = new[]
            {
                new { localId = "first", itemId = (Guid?)null, code = "FIRST", text = new { es = "Primera", en = (string?)null }, detail = new { es = (string?)null, en = (string?)null }, position = 1, isActive = true },
                new { localId = "second", itemId = (Guid?)null, code = "SECOND", text = new { es = "Segunda", en = (string?)null }, detail = new { es = (string?)null, en = (string?)null }, position = 1, isActive = true },
            },
        };

        await ExportRequestBodyAsync(duplicatePositions, "create-duplicate-positions-request");
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", duplicatePositions, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        using var listed = await client.GetAsync("/api/forms/catalogs?search=" + Uri.EscapeDataString(unique), Ct);
        Assert.Equal(0, (await listed.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Creating_manual_catalog_appends_items_in_request_order_without_client_positions()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var request = new
        {
            name = new { es = "Append", en = "Append" },
            description = new { es = "Prueba", en = "Test" },
            items = new[]
            {
                new { localId = "first", itemId = (Guid?)null, code = "FIRST", text = new { es = "Primera" }, detail = (object?)null, isActive = true },
                new { localId = "second", itemId = (Guid?)null, code = "SECOND", text = new { es = "Segunda" }, detail = (object?)null, isActive = true },
            },
        };

        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var response = await created.ReadJsonAsync();
        var catalogId = response.GetProperty("catalog").GetProperty("id").GetGuid();
        var firstId = response.GetProperty("itemIdentityMap").GetProperty("first").GetGuid();
        var secondId = response.GetProperty("itemIdentityMap").GetProperty("second").GetGuid();
        using var listed = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?sort=position", Ct);
        var rows = (await listed.ReadJsonAsync()).GetProperty("items");
        Assert.Equal(firstId, rows[0].GetProperty("id").GetGuid());
        Assert.Equal(1, rows[0].GetProperty("position").GetInt32());
        Assert.Equal(secondId, rows[1].GetProperty("id").GetGuid());
        Assert.Equal(2, rows[1].GetProperty("position").GetInt32());
    }

    [Fact]
    public async Task Queued_manual_item_moves_cross_pages_and_commit_unique_positions_with_audit()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Orden", en = "Order" },
            description = new { es = "Prueba", en = "Test" },
            items = new[]
            {
                Item("first", null, "FIRST", "Primera", null, true),
                Item("second", null, "SECOND", "Segunda", null, true),
                Item("third", null, "THIRD", "Tercera", null, true),
                Item("fourth", null, "FOURTH", "Cuarta", null, true),
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdJson = await created.ReadJsonAsync();
        var catalogId = createdJson.GetProperty("catalog").GetProperty("id").GetGuid();
        var firstId = createdJson.GetProperty("itemIdentityMap").GetProperty("first").GetGuid();
        var secondId = createdJson.GetProperty("itemIdentityMap").GetProperty("second").GetGuid();
        var thirdId = createdJson.GetProperty("itemIdentityMap").GetProperty("third").GetGuid();
        var fourthId = createdJson.GetProperty("itemIdentityMap").GetProperty("fourth").GetGuid();

        using var firstPage = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=1&pageSize=1&sort=position", Ct);
        using var fourthPage = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=4&pageSize=1&sort=position", Ct);
        Assert.Equal(firstId, (await firstPage.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(fourthId, (await fourthPage.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());

        var queuedMovesRequest = new
        {
            expectedRevision = 1,
            name = new { es = "Orden", en = "Order" },
            description = new { es = "Prueba", en = "Test" },
            items = Array.Empty<object>(),
            moves = new[]
            {
                new { itemId = fourthId, direction = "up" },
                new { itemId = fourthId, direction = "up" },
                new { itemId = firstId, direction = "down" },
            },
        };
        await ExportRequestBodyAsync(queuedMovesRequest, "queued-moves-request");
        using var movedUp = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", queuedMovesRequest, Ct);

        Assert.Equal(HttpStatusCode.OK, movedUp.StatusCode);
        var actorId = Guid.Parse(client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        using var movedFirstPage = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=1&pageSize=1&sort=position", Ct);
        using var movedSecondPage = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=2&pageSize=1&sort=position", Ct);
        Assert.Equal(fourthId, (await movedFirstPage.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(firstId, (await movedSecondPage.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        using var movedRows = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?sort=position", Ct);
        var rows = (await movedRows.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { fourthId, firstId, secondId, thirdId }, rows.Select(row => row.GetProperty("id").GetGuid()));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(row => row.GetProperty("position").GetInt32()));
        var auditRows = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>()
            .Where(item => item.CatalogId == catalogId).Select(item => new { item.Id, item.Position, item.ModifiedAtUtc, item.ModifiedBy }).ToListAsync(Ct));
        Assert.Equal(4, auditRows.Select(item => item.Position).Distinct().Count());
        Assert.All(auditRows, item =>
        {
            Assert.NotNull(item.ModifiedAtUtc);
            Assert.Equal(actorId, item.ModifiedBy);
        });
    }

    [Fact]
    public async Task Moving_beyond_an_order_boundary_rolls_back_metadata_and_order()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Orden estable", en = "Stable order" },
            description = new { es = "Original", en = "Original" },
            items = new[]
            {
                Item("first", null, "FIRST", "Primera", null, true),
                Item("second", null, "SECOND", "Segunda", null, true),
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var createdJson = await created.ReadJsonAsync();
        var catalogId = createdJson.GetProperty("catalog").GetProperty("id").GetGuid();
        var firstId = createdJson.GetProperty("itemIdentityMap").GetProperty("first").GetGuid();
        var secondId = createdJson.GetProperty("itemIdentityMap").GetProperty("second").GetGuid();

        using var rejected = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", new
        {
            expectedRevision = 1,
            name = new { es = "No guardar", en = "Do not save" },
            description = new { es = "Cambio", en = "Change" },
            items = Array.Empty<object>(),
            moves = new[] { new { itemId = firstId, direction = "up" } },
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var error = await rejected.ReadJsonAsync();
        Assert.Equal(FormCatalogErrors.ContentInvalid.Code, error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("errors").TryGetProperty("moves[0].direction", out _));
        using var detail = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        var detailJson = await detail.ReadJsonAsync();
        Assert.Equal(1, detailJson.GetProperty("revision").GetInt64());
        Assert.Equal("Orden estable", detailJson.GetProperty("name").GetProperty("es").GetString());
        using var page1 = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=1&pageSize=1&sort=position", Ct);
        using var page2 = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?page=2&pageSize=1&sort=position", Ct);
        Assert.Equal(firstId, (await page1.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(secondId, (await page2.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
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

    private static object Item(string localId, Guid? itemId, string code, string textEs, string? textEn, bool active) => new
    {
        localId,
        itemId,
        code,
        text = new { es = textEs, en = textEn },
        detail = new { es = (string?)null, en = (string?)null },
        isActive = active,
    };
}
