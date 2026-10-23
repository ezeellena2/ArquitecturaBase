using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormCatalogEndpointsTests
{
    [Fact]
    public async Task Extra_fields_keep_typed_json_and_reject_required_conversion_and_unique_failures_atomically()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Fields " + Guid.NewGuid().ToString("N"), en = "Fields" };
        var createBody = new
        {
            name = texts, description = texts,
            fields = new[] { FieldPayload(null, "integer") },
            items = new[] { Item("one", null, "ONE", "One", null, true) },
        };
        await ExportRequestBodyAsync(createBody, "fields-create");
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", createBody, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await ExportBilingualResponseAsync(created, "fields-created");
        var saved = await created.ReadJsonAsync();
        var catalogId = saved.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = saved.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        var itemId = saved.GetProperty("itemIdentityMap").GetProperty("one").GetGuid();
        var path = $"/api/forms/catalogs/{catalogId}";
        object Update(long revision, object field, params object[] items) => new { expectedRevision = revision, name = texts, description = texts, fields = new[] { field }, items };

        using var required = await client.PutAsJsonAsync(path, Update(1, FieldPayload(fieldId, "integer", required: true)), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, required.StatusCode);
        var missing = await required.ReadJsonAsync();
        Assert.Equal("Forms.Catalog.FieldValuesMissing", missing.GetProperty("code").GetString());
        Assert.Equal(1, missing.GetProperty("missingCount").GetInt32());
        Assert.True(missing.GetProperty("errors").TryGetProperty("fields[0].required", out _));
        await ExportBilingualResponseAsync(required, "fields-required-error");

        var valueBody = Update(1, FieldPayload(fieldId, "integer"), FieldItem("one", itemId, "ONE", fieldId, "0001"));
        await ExportRequestBodyAsync(valueBody, "fields-update");
        using var values = await client.PutAsJsonAsync(path, valueBody, Ct);
        Assert.Equal(HttpStatusCode.OK, values.StatusCode);
        await ExportBilingualResponseAsync(values, "fields-values-saved");
        var raw = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().Where(item => item.Id == itemId).Select(item => item.FieldValuesJson).SingleAsync(Ct));
        Assert.Equal(JsonValueKind.Number, JsonDocument.Parse(raw).RootElement.GetProperty(fieldId.ToString("D")).ValueKind);
        Assert.Equal(1, JsonDocument.Parse(raw).RootElement.GetProperty(fieldId.ToString("D")).GetInt32());

        using var conversion = await client.PutAsJsonAsync(path, Update(2, FieldPayload(fieldId, "date")), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, conversion.StatusCode);
        var failedConversion = await conversion.ReadJsonAsync();
        Assert.Equal("Forms.Catalog.FieldTypeNotConvertible", failedConversion.GetProperty("code").GetString());
        Assert.Equal(0, failedConversion.GetProperty("convertedCount").GetInt32());
        Assert.Equal(itemId, Assert.Single(failedConversion.GetProperty("invalidRows").EnumerateArray()).GetProperty("itemId").GetGuid());
        await ExportBilingualResponseAsync(conversion, "fields-conversion-error");

        using var rules = await client.PutAsJsonAsync(path, Update(2, FieldPayload(fieldId, "integer", required: true, unique: true)), Ct);
        Assert.Equal(HttpStatusCode.OK, rules.StatusCode);
        using var duplicate = await client.PutAsJsonAsync(path, Update(3, FieldPayload(fieldId, "integer", required: true, unique: true), FieldItem("new", null, "TWO", fieldId, "1")), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        var duplicateError = await duplicate.ReadJsonAsync();
        Assert.Equal("Forms.Catalog.FieldValueDuplicate", duplicateError.GetProperty("code").GetString());
        Assert.True(duplicateError.GetProperty("errors").TryGetProperty("items[0].fields." + fieldId.ToString("D"), out _));
        await ExportBilingualResponseAsync(duplicate, "fields-unique-error");

        using var detail = await client.GetAsync(path, Ct);
        var current = await detail.ReadJsonAsync();
        Assert.Equal(3, current.GetProperty("revision").GetInt64());
        Assert.True(current.GetProperty("fields")[0].GetProperty("hasValues").GetBoolean());
        Assert.Equal("integer", current.GetProperty("fields")[0].GetProperty("type").GetString());
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().CountAsync(item => item.CatalogId == catalogId, Ct)));
        await ExportBilingualResponseAsync(detail, "fields-detail");
        using var itemPage = await client.GetAsync(path + "/items", Ct);
        Assert.Equal("1", (await itemPage.ReadJsonAsync()).GetProperty("items")[0].GetProperty("fields").GetProperty(fieldId.ToString("D")).GetString());
        await ExportBilingualResponseAsync(itemPage, "fields-items");
    }

    [Fact]
    public async Task Removing_a_field_confirms_values_and_keeps_its_historical_name_and_data()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Remove " + Guid.NewGuid().ToString("N"), en = "Remove" };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts, fields = new[] { FieldPayload(null, "text") }, items = new[] { Item("one", null, "ONE", "One", null, true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var id = saved.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = saved.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        var itemId = saved.GetProperty("itemIdentityMap").GetProperty("one").GetGuid();
        var path = $"/api/forms/catalogs/{id}";
        using var added = await client.PutAsJsonAsync(path, new { expectedRevision = 1, name = texts, description = texts, fields = new[] { FieldPayload(fieldId, "text") }, items = new[] { FieldItem("one", itemId, "ONE", fieldId, "Value") } }, Ct);
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        object Remove(bool confirm) => new { expectedRevision = 2, name = texts, description = texts, fields = new[] { FieldPayload(fieldId, "text", active: false) }, items = Array.Empty<object>(), confirmFieldRemovals = confirm ? new[] { fieldId } : Array.Empty<Guid>() };
        using var rejected = await client.PutAsJsonAsync(path, Remove(false), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal("Forms.Catalog.FieldRemovalNotConfirmed", (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
        await ExportBilingualResponseAsync(rejected, "fields-removal-confirmation");
        using var removed = await client.PutAsJsonAsync(path, Remove(true), Ct);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        var field = (await removed.ReadJsonAsync()).GetProperty("catalog").GetProperty("fields")[0];
        Assert.False(field.GetProperty("isActive").GetBoolean());
        Assert.Equal("Field", field.GetProperty("name").GetProperty("es").GetString());
        var raw = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().Where(item => item.Id == itemId).Select(item => item.FieldValuesJson).SingleAsync(Ct));
        Assert.Equal("Value", JsonDocument.Parse(raw).RootElement.GetProperty(fieldId.ToString("D")).GetString());
        await ExportBilingualResponseAsync(removed, "fields-removed");
    }

    [Fact]
    public async Task Source_catalogs_cannot_be_deactivated_and_composed_long_titles_can_be_saved_and_checked()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Source " + Guid.NewGuid().ToString("N"), en = "Source" };
        using var sourceResponse = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts, items = new[] { Item("option", null, "CHOICE", "A source choice", "Source choice", true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, sourceResponse.StatusCode);
        var source = await sourceResponse.ReadJsonAsync();
        var sourceId = source.GetProperty("catalog").GetProperty("id").GetGuid();
        var optionId = source.GetProperty("itemIdentityMap").GetProperty("option").GetGuid();
        using var dependentResponse = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts, fields = new[] { FieldPayload(null, "catalog", sourceId) }, items = new[] { Item("one", null, "ONE", new string('x', 25), null, true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, dependentResponse.StatusCode);
        var dependent = await dependentResponse.ReadJsonAsync();
        var id = dependent.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = dependent.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        var itemId = dependent.GetProperty("itemIdentityMap").GetProperty("one").GetGuid();
        var path = $"/api/forms/catalogs/{id}";
        using var saved = await client.PutAsJsonAsync(path, new { expectedRevision = 1, name = texts, description = texts, fields = new[] { FieldPayload(fieldId, "catalog", sourceId) }, presentation = new { title = new[] { "text", fieldId.ToString("D") }, description = Array.Empty<string>() }, items = new[] { FieldItem("one", itemId, "ONE", fieldId, optionId.ToString("D"), new string('x', 25)) } }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var catalog = (await saved.ReadJsonAsync()).GetProperty("catalog");
        Assert.Equal(1, catalog.GetProperty("presentationCheck").GetProperty("titleTooLongCount").GetInt32());
        await ExportBilingualResponseAsync(saved, "fields-presentation");
        using var items = await client.GetAsync(path + "/items", Ct);
        var choice = (await items.ReadJsonAsync()).GetProperty("items")[0];
        Assert.Equal("A source choice", choice.GetProperty("fieldTexts").GetProperty(fieldId.ToString("D")).GetProperty("es").GetString());
        await ExportBilingualResponseAsync(items, "fields-source-items");
        using var blocked = await client.PutAsJsonAsync($"/api/forms/catalogs/{sourceId}", new { expectedRevision = 1, name = texts, description = texts, items = Array.Empty<object>(), isActive = false }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("Forms.Catalog.SourceInUse", (await blocked.ReadJsonAsync()).GetProperty("code").GetString());
        await ExportBilingualResponseAsync(blocked, "fields-source-in-use");
    }

    private static object FieldPayload(Guid? id, string type, Guid? sourceId = null, bool required = false, bool unique = false, bool active = true) => new
    {
        localId = id is null ? "field" : null, id, key = "field", name = new { es = "Field", en = "Field" }, type,
        sourceCatalogId = sourceId, required, unique, min = (string?)null, max = (string?)null, maxLength = type == "text" ? 300 : (int?)null,
        role = "data", position = 0, isActive = active,
    };

    private static object FieldItem(string localId, Guid? id, string code, Guid fieldId, string? value, string text = "One") => new
    {
        localId, itemId = id, code, text = new { es = text, en = text }, isActive = true,
        fields = new Dictionary<string, string?> { [fieldId.ToString("D")] = value },
    };
}
