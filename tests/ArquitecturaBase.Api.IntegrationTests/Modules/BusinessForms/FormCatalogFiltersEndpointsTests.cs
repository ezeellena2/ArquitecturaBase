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
public sealed class FormCatalogFiltersEndpointsTests(ApiFactory factory)
{
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

        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsManage);
        using var options = await manager.GetAsync("/api/forms/catalog-filter-options?page=1&pageSize=20", Ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
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
        using var preview = await manager.PostAsJsonAsync("/api/forms/catalog-previews", new
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
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var previewJson = await preview.ReadJsonAsync();
        Assert.Equal(1, previewJson.GetProperty("totalCount").GetInt32());
        Assert.False(previewJson.GetProperty("exceedsLimit").GetBoolean());
        var item = Assert.Single(previewJson.GetProperty("items").EnumerateArray());
        Assert.Equal(expectedId, item.GetProperty("id").GetGuid());
        Assert.Equal("Beatriz Coincide " + suffix, item.GetProperty("text").GetString());
        Assert.Equal(supervisorRole + ", " + technicianRole, item.GetProperty("detail").GetString());
        Assert.Equal(catalogCountBefore, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().CountAsync(Ct)));
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
}
