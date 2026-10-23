using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormPersonDataHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Person_data_uses_its_own_revision_and_user_managers_can_select_values_without_catalog_read_access(string culture)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.PersonDataManage, Permissions.Users.Read, Permissions.Users.Manage);
        manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var catalogResponse = await manager.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Zonas " + suffix, en = "Zones " + suffix },
            description = new { es = "Zonas del piloto", en = "Pilot zones" },
            items = new[]
            {
                Item("central", "CENTRAL", "Centro", "Central", true),
                Item("south", "SOUTH", "Sur", "South", true),
                Item("old", "OLD", "Retirada", "Retired", false),
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, catalogResponse.StatusCode);
        var catalog = (await catalogResponse.ReadJsonAsync()).GetProperty("catalog");
        var catalogId = catalog.GetProperty("id").GetGuid();
        var items = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == catalogId)
            .ToDictionaryAsync(item => item.Code, item => item.Id, Ct));

        using var attributeCreated = await manager.PostAsJsonAsync("/api/forms/person-attributes", new
        {
            key = "zone_" + suffix, name = new { es = "Zona", en = "Zone" }, type = "Catalog", sourceCatalogId = catalogId,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, attributeCreated.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(attributeCreated, culture, "attribute-created", Ct);
        var attributeId = (await attributeCreated.ReadJsonAsync()).GetProperty("id").GetGuid();
        var userId = await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("person-data-subject")).Value,
                null, false, "Persona E2 " + suffix, "es", Ct);
            return user.Id;
        });

        using var editor = await OperatorAsync(Permissions.Users.Manage);
        editor.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var catalogForbidden = await editor.GetAsync($"/api/forms/catalogs/{catalogId}/items", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, catalogForbidden.StatusCode);
        using var sourceOptions = await editor.GetAsync($"/api/forms/person-attributes/{attributeId}/options?page=1&pageSize=20", Ct);
        Assert.Equal(HttpStatusCode.OK, sourceOptions.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(sourceOptions, culture, "person-options", Ct);
        var options = await sourceOptions.ReadJsonAsync();
        Assert.Equal(2, options.GetProperty("totalCount").GetInt32());
        Assert.Contains(options.GetProperty("items").EnumerateArray(), option => option.GetProperty("id").GetGuid() == items["CENTRAL"]
            && option.GetProperty("text").GetString() == (culture == "en" ? "Central" : "Centro"));
        Assert.All(options.GetProperty("items").EnumerateArray(), option => Assert.Equal(2, option.EnumerateObject().Count()));

        using var empty = await editor.GetAsync($"/api/forms/person-data/{userId}", Ct);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(empty, culture, "person-data-empty", Ct);
        var emptyJson = await empty.ReadJsonAsync();
        Assert.Equal(JsonValueKind.Null, emptyJson.GetProperty("revision").ValueKind);
        Assert.Empty(emptyJson.GetProperty("values").EnumerateObject());

        var values = new Dictionary<string, string?> { [attributeId.ToString("D")] = items["CENTRAL"].ToString("D") };
        using var saved = await editor.PutAsJsonAsync($"/api/forms/person-data/{userId}", new { expectedRevision = (long?)null, values }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(saved, culture, "person-data-saved", Ct);
        var savedJson = await saved.ReadJsonAsync();
        Assert.Equal(1, savedJson.GetProperty("revision").GetInt64());
        Assert.Equal(items["CENTRAL"].ToString("D"), savedJson.GetProperty("values").GetProperty(attributeId.ToString("D")).GetProperty("value").GetString());

        using var detail = await editor.GetAsync($"/api/forms/person-data/{userId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(detail, culture, "person-data", Ct);
        Assert.Equal(culture == "en" ? "Central" : "Centro", (await detail.ReadJsonAsync()).GetProperty("values")
            .GetProperty(attributeId.ToString("D")).GetProperty("text").GetString());
        using var people = await manager.GetAsync("/api/forms/person-data?page=1&pageSize=20&search=" + Uri.EscapeDataString("Persona E2 " + suffix), Ct);
        Assert.Equal(HttpStatusCode.OK, people.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(people, culture, "people", Ct);
        Assert.Equal(userId, Assert.Single((await people.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("userId").GetGuid());

        using var stale = await editor.PutAsJsonAsync($"/api/forms/person-data/{userId}", new { expectedRevision = (long?)null, values }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Forms.PersonData.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        await FormPersonDataContractCapture.WriteAsync(stale, culture, "conflict", Ct);
        using var invalid = await editor.PutAsJsonAsync($"/api/forms/person-data/{userId}", new
        {
            expectedRevision = 1, values = new Dictionary<string, string?> { [attributeId.ToString("D")] = items["OLD"].ToString("D") },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("Forms.PersonData.ValueInvalid", (await invalid.ReadJsonAsync()).GetProperty("code").GetString());
        await FormPersonDataContractCapture.WriteAsync(invalid, culture, "invalid-value", Ct);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPersonData>()
            .Where(row => row.UserId == userId).Select(row => row.Revision).SingleAsync(Ct)));

        using var updated = await manager.PutAsJsonAsync($"/api/forms/person-attributes/{attributeId}", new
        {
            expectedRevision = 1, name = new { es = "Zona de atención", en = "Service zone" }, type = "Catalog",
            sourceCatalogId = catalogId, position = 1, isActive = true,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(2, (await updated.ReadJsonAsync()).GetProperty("revision").GetInt64());
        await FormPersonDataContractCapture.WriteAsync(updated, culture, "attribute-updated", Ct);
        using var attributes = await editor.GetAsync("/api/forms/person-attributes", Ct);
        Assert.Equal(HttpStatusCode.OK, attributes.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(attributes, culture, "attributes", Ct);
        Assert.Contains((await attributes.ReadJsonAsync()).GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == attributeId);

        using var forbiddenReader = await OperatorAsync(BusinessFormsPermissions.PersonDataManage);
        forbiddenReader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        using var forbidden = await forbiddenReader.GetAsync("/api/forms/person-data", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        await FormPersonDataContractCapture.WriteAsync(forbidden, culture, "forbidden", Ct);
        using var sourceInUse = await manager.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}", new
        {
            expectedRevision = 1, name = new { es = "Zonas " + suffix, en = "Zones " + suffix },
            description = new { es = "Zonas del piloto", en = "Pilot zones" }, items = Array.Empty<object>(), isActive = false,
        }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, sourceInUse.StatusCode);
        Assert.Equal("Forms.Catalog.SourceInUse", (await sourceInUse.ReadJsonAsync()).GetProperty("code").GetString());
        await FormPersonDataContractCapture.WriteAsync(sourceInUse, culture, "source-in-use", Ct);

        object ToText(bool confirm) => new
        {
            expectedRevision = 2, name = new { es = "Zona de atención", en = "Service zone" }, type = "Text",
            sourceCatalogId = (Guid?)null, position = 1, isActive = true, confirmValueRemoval = confirm,
        };
        using var unconfirmed = await manager.PutAsJsonAsync($"/api/forms/person-attributes/{attributeId}", ToText(false), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        var unconfirmedJson = await unconfirmed.ReadJsonAsync();
        Assert.Equal("Forms.PersonAttribute.ValueRemovalNotConfirmed", unconfirmedJson.GetProperty("code").GetString());
        Assert.True(unconfirmedJson.GetProperty("errors").TryGetProperty("type", out _));
        Assert.Equal(1, unconfirmedJson.GetProperty("count").GetInt32());
        await FormPersonDataContractCapture.WriteAsync(unconfirmed, culture, "attribute-value-removal-confirmation", Ct);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPersonData>()
            .Where(row => row.UserId == userId).Select(row => row.Revision).SingleAsync(Ct)));

        using var confirmed = await manager.PutAsJsonAsync($"/api/forms/person-attributes/{attributeId}", ToText(true), Ct);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Text", (await confirmed.ReadJsonAsync()).GetProperty("type").GetString());
        var cleared = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPersonData>()
            .Where(row => row.UserId == userId).Select(row => new { row.Revision, row.ValuesJson }).SingleAsync(Ct));
        Assert.Equal(2, cleared.Revision);
        Assert.False(JsonDocument.Parse(cleared.ValuesJson).RootElement.TryGetProperty(attributeId.ToString("D"), out _));
    }

    [Fact]
    public async Task Changing_an_attribute_type_asks_confirmation_only_for_people_who_are_not_deleted_and_still_clears_deleted_accounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.PersonDataManage, Permissions.Users.Manage);
        using var catalogResponse = await manager.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Zonas borradas " + suffix, en = "Deleted zones " + suffix },
            description = new { es = "Zonas", en = "Zones" }, items = new[] { Item("central", "CENTRAL", "Centro", "Central", true) },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, catalogResponse.StatusCode);
        var catalogId = (await catalogResponse.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var itemId = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == catalogId)
            .Select(item => item.Id).SingleAsync(Ct));
        using var created = await manager.PostAsJsonAsync("/api/forms/person-attributes", new
        {
            key = "deleted_zone_" + suffix, name = new { es = "Zona", en = "Zone" }, type = "Catalog", sourceCatalogId = catalogId,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var attribute = await created.ReadJsonAsync();
        var attributeId = attribute.GetProperty("id").GetGuid();
        var key = attributeId.ToString("D");
        var (visible, deleted) = await factory.InTransactionAsync(async services =>
        {
            var users = services.GetRequiredService<IUserRepository>();
            var first = await users.CreateAsync(Email.Create(TestEmails.Unique("person-data-visible")).Value, null, false, "Persona visible " + suffix, "es", Ct);
            var second = await users.CreateAsync(Email.Create(TestEmails.Unique("person-data-deleted")).Value, null, false, "Persona borrada " + suffix, "es", Ct);
            return (first.Id, second.Id);
        });
        foreach (var userId in new[] { visible, deleted })
        {
            using var saved = await manager.PutAsJsonAsync($"/api/forms/person-data/{userId}", new
            {
                expectedRevision = (long?)null, values = new Dictionary<string, string?> { [key] = itemId.ToString("D") },
            }, Ct);
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        }
        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().DeleteAsync(deleted, Ct));
        var toText = new
        {
            expectedRevision = 1, name = new { es = "Zona", en = "Zone" }, type = "Text", sourceCatalogId = (Guid?)null,
            position = attribute.GetProperty("position").GetInt32(), isActive = true,
        };

        using var visibleAndDeleted = await manager.PutAsJsonAsync($"/api/forms/person-attributes/{attributeId}", toText, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, visibleAndDeleted.StatusCode);
        Assert.Equal(1, (await visibleAndDeleted.ReadJsonAsync()).GetProperty("count").GetInt32());

        using var cleared = await manager.PutAsJsonAsync($"/api/forms/person-data/{visible}", new
        {
            expectedRevision = 1, values = new Dictionary<string, string?> { [key] = null },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        using var onlyDeleted = await manager.PutAsJsonAsync($"/api/forms/person-attributes/{attributeId}", toText, Ct);

        Assert.Equal(HttpStatusCode.OK, onlyDeleted.StatusCode);
        Assert.Equal("Text", (await onlyDeleted.ReadJsonAsync()).GetProperty("type").GetString());
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormPersonData>().Where(row => row.UserId == deleted)
            .Select(row => new { row.Revision, row.ValuesJson }).SingleAsync(Ct));
        Assert.Equal(2, stored.Revision);
        Assert.False(JsonDocument.Parse(stored.ValuesJson).RootElement.TryGetProperty(key, out _));
    }

    [Fact]
    public async Task Person_data_routes_require_authentication_and_dedicated_permissions_without_granting_user_read_access()
    {
        var id = Guid.CreateVersion7();
        using var anonymous = factory.CreateClient();
        foreach (var route in new[] { "/api/forms/person-attributes", $"/api/forms/person-attributes/{id}/options", "/api/forms/person-data", $"/api/forms/person-data/{id}" })
        {
            using var response = await anonymous.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using var manager = await OperatorAsync(BusinessFormsPermissions.PersonDataManage);
        using var list = await manager.GetAsync("/api/forms/person-attributes", Ct);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var individual = await manager.GetAsync($"/api/forms/person-data/{id}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, individual.StatusCode);
        using var usersOnly = await OperatorAsync(Permissions.Users.Read);
        using var attributes = await usersOnly.GetAsync("/api/forms/person-attributes", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, attributes.StatusCode);
    }

    private async Task<HttpClient> OperatorAsync(params string[] permissions)
    {
        var actor = await factory.InTransactionAsync(async services =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            var roleName = "person-data-operator-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(roleName, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("person-data-operator")).Value,
                null, false, "Operador E2", "es", Ct);
            await users.SetRolesAsync(user.Id, [roleName], Ct);
            return user.Id;
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, actor.ToString());
        return client;
    }

    private static object Item(string localId, string code, string es, string en, bool active) => new
    {
        localId, code, text = new { es, en }, detail = (object?)null, isActive = active,
    };
}
