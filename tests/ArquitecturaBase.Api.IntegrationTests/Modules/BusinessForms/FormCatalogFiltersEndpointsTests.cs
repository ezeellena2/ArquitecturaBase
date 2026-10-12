using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogFiltersEndpointsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Filter_options_and_preview_use_the_selected_sample_and_role_membership()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var supervisorRole = "K4 supervisor " + suffix;
        var technicianRole = "K4 technician " + suffix;
        var sampleId = Guid.Empty;
        var expectedId = Guid.Empty;
        await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await roles.CreateAsync(supervisorRole, null, [], Ct);
            await roles.CreateAsync(technicianRole, null, [], Ct);

            var users = services.GetRequiredService<IUserRepository>();
            var sample = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-sample")).Value,
                null, false, "Ana Muestra " + suffix, "es", Ct);
            var expected = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-match")).Value,
                null, false, "Beatriz Coincide " + suffix, "es", Ct);
            var otherRole = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-other-role")).Value,
                null, false, "Carlos Otro " + suffix, "es", Ct);
            var inactive = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-inactive")).Value,
                null, false, "Diana Inactiva " + suffix, "es", Ct);

            await users.SetRolesAsync(sample.Id, [supervisorRole], Ct);
            await users.SetRolesAsync(expected.Id, [supervisorRole, technicianRole], Ct);
            await users.SetRolesAsync(otherRole.Id, [technicianRole], Ct);
            await users.SetRolesAsync(inactive.Id, [supervisorRole], Ct);
            sampleId = sample.Id;
            expectedId = expected.Id;
            await users.SetActiveAsync(inactive.Id, false, Ct);
        });
        await CreateUsersViewAsync();

        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var options = await manager.GetAsync("/api/forms/catalog-filter-options?page=1&pageSize=20", Ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        await ExportResponseAsync(options, "options");
        var optionsJson = await options.ReadJsonAsync();
        Assert.Equal("users", optionsJson.GetProperty("source").GetString());
        var roleColumn = Assert.Single(optionsJson.GetProperty("columns").EnumerateArray(), column =>
            column.GetProperty("key").GetString() == "role");
        Assert.Contains("has", roleColumn.GetProperty("operators").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(supervisorRole, roleColumn.GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("key").GetString()));
        var sampleRows = optionsJson.GetProperty("sampleUsers").GetProperty("items");
        var selectedSample = Assert.Single(sampleRows.EnumerateArray(), user => user.GetProperty("id").GetGuid() == sampleId);
        Assert.Equal("Ana Muestra " + suffix, selectedSample.GetProperty("name").GetString());
        Assert.False(selectedSample.TryGetProperty("email", out _));
        Assert.Equal(1, optionsJson.GetProperty("sampleUsers").GetProperty("page").GetInt32());

        var catalogCountBefore = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().CountAsync(Ct));
        var previewRequest = new
        {
            sampleUserId = sampleId,
            filters = new
            {
                textColumn = "name",
                detailColumn = "role",
                conditions = new object[]
                {
                    new { column = "role", @operator = "has", value = supervisorRole },
                    new { column = "active", @operator = "equals", value = "true" },
                },
                excludeRecipient = true,
                sortColumn = "name",
                sortDescending = false,
            },
        };
        await ExportRequestAsync(previewRequest, "preview-request");
        using var preview = await manager.PostAsJsonAsync("/api/forms/catalog-previews", previewRequest, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        await ExportResponseAsync(preview, "preview");
        var previewJson = await preview.ReadJsonAsync();
        Assert.Equal(1, previewJson.GetProperty("totalCount").GetInt32());
        Assert.False(previewJson.GetProperty("exceedsLimit").GetBoolean());
        var item = Assert.Single(previewJson.GetProperty("items").EnumerateArray());
        Assert.Equal(expectedId, item.GetProperty("id").GetGuid());
        Assert.Equal("Beatriz Coincide " + suffix, item.GetProperty("text").GetString());
        Assert.Equal(supervisorRole + ", " + technicianRole, item.GetProperty("detail").GetString());
        Assert.Equal(catalogCountBefore, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().CountAsync(Ct)));

        async Task<System.Text.Json.JsonElement> PreviewForAsync(FormCatalogFilterDefinition filters)
        {
            using var response = await manager.PostAsJsonAsync("/api/forms/catalog-previews",
                new FormCatalogFilterPreviewRequest(sampleId, filters), Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }

        var contains = await PreviewForAsync(new FormCatalogFilterDefinition("name", null,
            [new("name", "contains", "Coincide")], false, "name", false));
        Assert.Equal(expectedId, Assert.Single(contains.GetProperty("items").EnumerateArray())
            .GetProperty("id").GetGuid());

        var equals = await PreviewForAsync(new FormCatalogFilterDefinition("name", null,
            [new("name", "equals", "Beatriz Coincide " + suffix)], false, "name", false));
        Assert.Equal(expectedId, Assert.Single(equals.GetProperty("items").EnumerateArray())
            .GetProperty("id").GetGuid());

        var inactive = await PreviewForAsync(new FormCatalogFilterDefinition("name", null,
            [new("active", "equals", "false")], false, "name", false));
        Assert.Equal("Diana Inactiva " + suffix,
            Assert.Single(inactive.GetProperty("items").EnumerateArray()).GetProperty("text").GetString());

        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var spanishActivity = await PreviewForAsync(new FormCatalogFilterDefinition("active", "active", [], false, "name", false));
        Assert.Contains("Activo", spanishActivity.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("text").GetString()));
        Assert.Contains("Inactivo", spanishActivity.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("detail").GetString()));
        manager.DefaultRequestHeaders.AcceptLanguage.Clear();
        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var englishActivity = await PreviewForAsync(new FormCatalogFilterDefinition("active", "active", [], false, "name", false));
        Assert.Contains("Active", englishActivity.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("text").GetString()));
        Assert.Contains("Inactive", englishActivity.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("detail").GetString()));
        manager.DefaultRequestHeaders.AcceptLanguage.Clear();

        var descendingPage = await manager.GetAsync(
            "/api/forms/catalog-filter-options?page=1&pageSize=2&sort=-name&search=" + Uri.EscapeDataString(suffix), Ct);
        Assert.Equal(HttpStatusCode.OK, descendingPage.StatusCode);
        var descendingPageJson = await descendingPage.ReadJsonAsync();
        Assert.Equal(3, descendingPageJson.GetProperty("sampleUsers").GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "Carlos Otro " + suffix, "Beatriz Coincide " + suffix },
            descendingPageJson.GetProperty("sampleUsers").GetProperty("items").EnumerateArray()
                .Select(user => user.GetProperty("name").GetString()).ToArray());
        using var descendingNextPage = await manager.GetAsync(
            "/api/forms/catalog-filter-options?page=2&pageSize=2&sort=-name&search=" + Uri.EscapeDataString(suffix), Ct);
        Assert.Equal(HttpStatusCode.OK, descendingNextPage.StatusCode);
        Assert.Equal(new[] { "Ana Muestra " + suffix },
            (await descendingNextPage.ReadJsonAsync()).GetProperty("sampleUsers").GetProperty("items").EnumerateArray()
                .Select(user => user.GetProperty("name").GetString()).ToArray());

        using var reader = await OperatorAsync(BusinessFormsPermissions.CatalogsRead);
        using var readOnlyOptions = await reader.GetAsync("/api/forms/catalog-filter-options", Ct);
        Assert.Equal(HttpStatusCode.OK, readOnlyOptions.StatusCode);
        Assert.True((await readOnlyOptions.ReadJsonAsync()).GetProperty("sampleUsers").ValueKind == System.Text.Json.JsonValueKind.Null);

        var savedFilters = new FormCatalogFilterDefinition("name", null, [], true, "name", false);
        var createRequest = new
        {
            name = new { es = "Personas por filtro", en = "People by filter" },
            description = new { es = "Descripción del filtro", en = "Filter description" },
            filters = savedFilters,
        };
        await ExportRequestAsync(createRequest, "create-request");
        using var create = await manager.PostAsJsonAsync("/api/forms/catalogs/filters", createRequest, Ct);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        await ExportResponseAsync(create, "created");
        var saveJson = await create.ReadJsonAsync();
        var savedCatalog = saveJson.GetProperty("catalog");
        var catalogId = savedCatalog.GetProperty("id").GetGuid();
        Assert.Equal("Filters", savedCatalog.GetProperty("kind").GetString());
        Assert.Equal("name", savedCatalog.GetProperty("filters").GetProperty("textColumn").GetString());

        using var detail = await manager.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await ExportResponseAsync(detail, "detail");
        Assert.Equal("name", (await detail.ReadJsonAsync()).GetProperty("filters").GetProperty("textColumn").GetString());

        var updateRequest = new
        {
            expectedRevision = 1,
            name = new { es = "Personas activas", en = "Active people" },
            description = new { es = "Descripción del filtro", en = "Filter description" },
            filters = savedFilters with { ExcludeRecipient = false },
        };
        await ExportRequestAsync(updateRequest, "update-request");
        using var update = await manager.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}/filters", updateRequest, Ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        await ExportResponseAsync(update, "updated");
        Assert.Equal(2, (await update.ReadJsonAsync()).GetProperty("catalog").GetProperty("revision").GetInt64());

        using var staleUpdate = await manager.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}/filters", new
        {
            expectedRevision = 1,
            name = new { es = "Obsoleto", en = "Stale" },
            description = new { es = "Descripción", en = "Description" },
            filters = savedFilters,
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
    }

    [Fact]
    public async Task Invalidated_user_view_keeps_catalog_routes_available_and_returns_a_stable_reason()
    {
        await CreateUsersViewAsync();
        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var request = new
        {
            name = new { es = "K4 vista inválida", en = "K4 unavailable view" },
            description = new { es = "Fuente versionada", en = "Versioned source" },
            filters = new FormCatalogFilterDefinition("name", null, [], true, "name", false),
        };
        using var created = await manager.PostAsJsonAsync("/api/forms/catalogs/filters", request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalogId = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();

        await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlRawAsync("""
            DROP VIEW "BusinessFormCatalogUsers";
            CREATE VIEW "BusinessFormCatalogUsers" AS
            SELECT users."Id", COALESCE(users."DisplayName", '') AS "Name",
                   ARRAY[]::text[] AS "Roles"
            FROM "AspNetUsers" AS users WHERE NOT users."IsDeleted";
            """, Ct));
        try
        {
            using var list = await manager.GetAsync("/api/forms/catalogs?state=Unavailable", Ct);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var listRow = Assert.Single((await list.ReadJsonAsync()).GetProperty("items").EnumerateArray(),
                item => item.GetProperty("id").GetGuid() == catalogId);
            Assert.Equal("Unavailable", listRow.GetProperty("state").GetString());
            Assert.Equal("Forms.Catalog.SourceUnavailable", listRow.GetProperty("stateReason").GetString());

            using var detail = await manager.GetAsync("/api/forms/catalogs/" + catalogId, Ct);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            Assert.Equal("Forms.Catalog.SourceUnavailable", (await detail.ReadJsonAsync())
                .GetProperty("unavailableReasonCode").GetString());

            using var options = await manager.GetAsync("/api/forms/catalog-filter-options", Ct);
            Assert.Equal(HttpStatusCode.OK, options.StatusCode);
            var optionJson = await options.ReadJsonAsync();
            Assert.False(optionJson.GetProperty("isAvailable").GetBoolean());
            Assert.Equal("Forms.Catalog.SourceUnavailable", optionJson.GetProperty("unavailableReasonCode").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, optionJson.GetProperty("sampleUsers").ValueKind);

            using var preview = await manager.PostAsJsonAsync("/api/forms/catalog-previews",
                new FormCatalogFilterPreviewRequest(Guid.NewGuid(), request.filters), Ct);
            Assert.Equal(HttpStatusCode.Conflict, preview.StatusCode);
            Assert.Equal("Forms.Catalog.SourceUnavailable", (await preview.ReadJsonAsync()).GetProperty("code").GetString());

            using var manualList = await manager.GetAsync("/api/forms/catalogs?kind=Manual", Ct);
            Assert.Equal(HttpStatusCode.OK, manualList.StatusCode);
        }
        finally
        {
            await CreateUsersViewAsync();
        }
    }

    private async Task CreateUsersViewAsync() => await factory.ExecuteDbContextAsync(db => db.Database.ExecuteSqlRawAsync("""
        CREATE OR REPLACE VIEW "BusinessFormCatalogUsers" AS
        SELECT users."Id",
               COALESCE(users."DisplayName", '') AS "Name",
               ARRAY(SELECT roles."Name"
                     FROM "AspNetUserRoles" AS user_roles
                     JOIN "AspNetRoles" AS roles ON roles."Id" = user_roles."RoleId"
                     WHERE user_roles."UserId" = users."Id"
                     ORDER BY roles."Name")::text[] AS "Roles",
               users."IsActive" AND NOT users."IsDeleted" AS "Active"
        FROM "AspNetUsers" AS users
        WHERE NOT users."IsDeleted";
        """, Ct));

    private async Task<HttpClient> OperatorAsync(params string[] permissions)
    {
        var actor = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var roleName = "catalog-filter-operator-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(roleName, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-operator")).Value,
                null, false, "Operador filtro", "es", Ct);
            await users.SetRolesAsync(user.Id, [roleName], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return client;
    }

    private static async Task ExportResponseAsync(HttpResponseMessage response, string name)
    {
        var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, "catalog-filters");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), await response.Content.ReadAsByteArrayAsync(Ct), Ct);
    }

    private static async Task ExportRequestAsync(object request, string name)
    {
        var output = Environment.GetEnvironmentVariable("FORMS_HTTP_CONTRACT_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        var directory = Path.Combine(output, "catalog-filters");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"),
            JsonSerializer.SerializeToUtf8Bytes(request, ExportJsonOptions), Ct);
    }
}
