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
        var emptyRole = "K4 empty " + suffix;
        var sampleId = Guid.Empty;
        var expectedId = Guid.Empty;
        var supervisorRoleId = Guid.Empty;
        var emptyRoleId = Guid.Empty;
        await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            supervisorRoleId = await roles.CreateAsync(supervisorRole, null, [], Ct);
            await roles.CreateAsync(technicianRole, null, [], Ct);
            emptyRoleId = await roles.CreateAsync(emptyRole, null, [], Ct);

            var users = services.GetRequiredService<IUserRepository>();
            var sample = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-sample")).Value,
                null, false, "Ana Muestra " + suffix, "es", Ct);
            var expected = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-match")).Value,
                null, false, "Beatriz Coincide " + suffix, "es", Ct);
            var otherRole = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-other-role")).Value,
                null, false, "Carlos Otro " + suffix, "es", Ct);
            var inactive = await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-inactive")).Value,
                null, false, "Diana Inactiva " + suffix, "es", Ct);
            await users.CreateAsync(Email.Create(TestEmails.Unique("catalog-filter-empty-name")).Value,
                null, false, null, "es", Ct);

            await users.SetRolesAsync(sample.Id, [supervisorRole], Ct);
            await users.SetRolesAsync(expected.Id, [supervisorRole, technicianRole], Ct);
            await users.SetRolesAsync(otherRole.Id, [technicianRole], Ct);
            await users.SetRolesAsync(inactive.Id, [supervisorRole], Ct);
            sampleId = sample.Id;
            expectedId = expected.Id;
            await users.SetActiveAsync(inactive.Id, false, Ct);
        });
        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var options = await manager.GetAsync("/api/forms/catalog-filter-options?page=1&pageSize=20&search=" + Uri.EscapeDataString(suffix), Ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        await ExportResponseAsync(options, "options");
        var optionsJson = await options.ReadJsonAsync();
        Assert.Equal("users", optionsJson.GetProperty("source").GetString());
        var roleColumn = Assert.Single(optionsJson.GetProperty("columns").EnumerateArray(), column =>
            column.GetProperty("key").GetString() == "role");
        Assert.Contains("has", roleColumn.GetProperty("operators").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(roleColumn.GetProperty("choices").EnumerateArray(), choice =>
            choice.GetProperty("key").GetString() == supervisorRoleId.ToString("D")
            && choice.GetProperty("label").GetString() == supervisorRole);
        Assert.Contains(roleColumn.GetProperty("choices").EnumerateArray(), choice =>
            choice.GetProperty("key").GetString() == emptyRoleId.ToString("D")
            && choice.GetProperty("label").GetString() == emptyRole);
        var sampleRows = optionsJson.GetProperty("sampleUsers").GetProperty("items");
        var selectedSample = Assert.Single(sampleRows.EnumerateArray(), user => user.GetProperty("id").GetGuid() == sampleId);
        Assert.Equal("Ana Muestra " + suffix, selectedSample.GetProperty("name").GetString());
        Assert.False(selectedSample.TryGetProperty("email", out _));
        Assert.Equal(1, optionsJson.GetProperty("sampleUsers").GetProperty("page").GetInt32());
        using var allSampleUsers = await manager.GetAsync("/api/forms/catalog-filter-options?page=1&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, allSampleUsers.StatusCode);
        var nonemptyActiveUsers = await factory.ExecuteDbContextAsync(db => db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::integer AS "Value"
            FROM "AspNetUsers"
            WHERE "IsActive" AND NOT "IsDeleted"
              AND NULLIF(BTRIM(COALESCE("DisplayName", '')), '') IS NOT NULL
            """).SingleAsync(Ct));
        Assert.Equal(nonemptyActiveUsers, (await allSampleUsers.ReadJsonAsync())
            .GetProperty("sampleUsers").GetProperty("totalCount").GetInt32());

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
                    new { column = "role", @operator = "has", value = supervisorRoleId.ToString("D") },
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

        using var invalidRolePreview = await manager.PostAsJsonAsync("/api/forms/catalog-previews",
            new FormCatalogFilterPreviewRequest(sampleId,
                new FormCatalogFilterDefinition("name", null, [new("role", "has", "supervisor")], false, "name", false)), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalidRolePreview.StatusCode);

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
            [new("name", "equals", "Diana Inactiva " + suffix), new("active", "equals", "false")], false, "name", false));
        Assert.Equal("Diana Inactiva " + suffix,
            Assert.Single(inactive.GetProperty("items").EnumerateArray()).GetProperty("text").GetString());

        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var spanishActivity = await PreviewForAsync(new FormCatalogFilterDefinition("active", "active",
            [new("name", "contains", suffix)], false, "name", false));
        Assert.Contains("Activo", spanishActivity.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("text").GetString()));
        Assert.Contains("Inactivo", spanishActivity.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("detail").GetString()));
        manager.DefaultRequestHeaders.AcceptLanguage.Clear();

        var renamedSupervisorRole = "K4 lead " + suffix;
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IRoleRepository>().UpdateAsync(supervisorRoleId, renamedSupervisorRole, null, [], Ct);
        });
        using var renamedOptions = await manager.GetAsync("/api/forms/catalog-filter-options?page=1&pageSize=20", Ct);
        var renamedRoleColumn = Assert.Single((await renamedOptions.ReadJsonAsync()).GetProperty("columns").EnumerateArray(), column =>
            column.GetProperty("key").GetString() == "role");
        Assert.Contains(renamedRoleColumn.GetProperty("choices").EnumerateArray(), choice =>
            choice.GetProperty("key").GetString() == supervisorRoleId.ToString("D")
            && choice.GetProperty("label").GetString() == renamedSupervisorRole);
        var renamedRolePreview = await PreviewForAsync(new FormCatalogFilterDefinition("name", null,
            [new("name", "equals", "Beatriz Coincide " + suffix), new("role", "has", supervisorRoleId.ToString("D"))], false, "name", false));
        Assert.Equal(expectedId, Assert.Single(renamedRolePreview.GetProperty("items").EnumerateArray())
            .GetProperty("id").GetGuid());
        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        var englishActivity = await PreviewForAsync(new FormCatalogFilterDefinition("active", "active",
            [new("name", "contains", suffix)], false, "name", false));
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
