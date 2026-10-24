using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormCatalogEndpointsTests
{
    [Theory]
    [InlineData("Text", null, null)]
    [InlineData("Integer", "1500", "1501")]
    [InlineData("Decimal", "1500.25", "0")]
    [InlineData("Date", "2026-10-22", "2026-10-23")]
    public async Task Typed_manual_catalogs_round_trip_the_fixed_type_and_editable_canonical_value(string type, string? value, string? updatedValue)
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var name = "Typed " + Guid.NewGuid().ToString("N");
        var texts = new { es = name, en = name };
        object ItemValue(Guid? id, string? current) => new
        {
            localId = "one", itemId = id, code = "NORTH", text = new { es = "Norte", en = "North" },
            detail = new { es = "Zona", en = "Area" }, isActive = true, value = current,
        };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs",
            new { name = texts, description = texts, valueType = type, items = new[] { ItemValue(null, value) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await ExportBilingualResponseAsync(created, "typed-" + type.ToLowerInvariant() + "-created");
        var body = await created.ReadJsonAsync();
        var catalogId = body.GetProperty("catalog").GetProperty("id").GetGuid();
        var itemId = body.GetProperty("itemIdentityMap").GetProperty("one").GetGuid();
        Assert.Equal(type, body.GetProperty("catalog").GetProperty("valueType").GetString());
        using var items = await client.GetAsync($"/api/forms/catalogs/{catalogId}/items?sort=position", Ct);
        Assert.Equal(HttpStatusCode.OK, items.StatusCode);
        await ExportBilingualResponseAsync(items, "typed-" + type.ToLowerInvariant() + "-items");
        var item = Assert.Single((await items.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(value, item.GetProperty("value").GetString());
        Assert.Equal(itemId, item.GetProperty("id").GetGuid());
        using var detail = await client.GetAsync($"/api/forms/catalogs/{catalogId}", Ct);
        Assert.Equal(type, (await detail.ReadJsonAsync()).GetProperty("valueType").GetString());
        using var page = await client.GetAsync("/api/forms/catalogs?search=" + Uri.EscapeDataString(name), Ct);
        var listed = Assert.Single((await page.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(type, listed.GetProperty("valueType").GetString());
        using var updated = await client.PutAsJsonAsync($"/api/forms/catalogs/{catalogId}",
            new { expectedRevision = 1, name = texts, description = texts, items = new[] { ItemValue(itemId, updatedValue) } }, Ct);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(type, (await updated.ReadJsonAsync()).GetProperty("catalog").GetProperty("valueType").GetString());
        Assert.Equal(updatedValue, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>()
            .Where(stored => stored.Id == itemId).Select(stored => stored.Value).SingleAsync(Ct)));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Invalid_typed_values_report_their_row_even_when_the_item_is_inactive(string culture)
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var name = "Invalid " + Guid.NewGuid().ToString("N");
        var texts = new { es = name, en = name };
        using var response = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = texts, description = texts, valueType = "Decimal",
            items = new[]
            {
                new { localId = "one", code = "ONE", text = new { es = "Uno", en = "One" }, isActive = true, value = "1500" },
                new { localId = "two", code = "TWO", text = new { es = "Dos", en = "Two" }, isActive = false, value = "1,5" },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExportBilingualResponseAsync(response, "typed-invalid-value-" + culture);
        var error = await response.ReadJsonAsync();
        Assert.Equal("Forms.Catalog.ItemValueInvalid", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("errors").TryGetProperty("items[1].value", out _));
        using var page = await client.GetAsync("/api/forms/catalogs?search=" + Uri.EscapeDataString(name), Ct);
        Assert.Equal(0, (await page.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Missing_typed_values_are_required_at_the_item_path()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var response = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { es = "Importe", en = "Amount" }, description = new { es = "Prueba", en = "Test" },
            valueType = "Decimal", items = new[] { new { localId = "one", code = "ONE", text = new { es = "Uno" }, isActive = true } },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await ExportBilingualResponseAsync(response, "typed-required-value");
        var error = await response.ReadJsonAsync();
        Assert.Equal("Forms.Catalog.ItemValueRequired", error.GetProperty("code").GetString());
        Assert.True(error.GetProperty("errors").TryGetProperty("items[0].value", out _));
    }

    [Fact]
    public async Task Updating_a_catalog_cannot_change_its_value_type_or_revision_through_an_unknown_member()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Importe fijo", en = "Fixed amount" };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs",
            new { name = texts, description = texts, valueType = "Decimal", items = Array.Empty<object>() }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        using var rejected = await client.PutAsJsonAsync($"/api/forms/catalogs/{id}",
            new { expectedRevision = 1, name = texts, description = texts, valueType = "Integer", items = Array.Empty<object>() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await ExportBilingualResponseAsync(rejected, "typed-type-change-rejected");
        using var detail = await client.GetAsync($"/api/forms/catalogs/{id}", Ct);
        var stored = await detail.ReadJsonAsync();
        Assert.Equal("Decimal", stored.GetProperty("valueType").GetString());
        Assert.Equal(1, stored.GetProperty("revision").GetInt64());
    }
}
