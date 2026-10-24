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
        var invalidRow = Assert.Single(failedConversion.GetProperty("invalidRows").EnumerateArray());
        Assert.Equal(itemId, invalidRow.GetProperty("itemId").GetGuid());
        Assert.Equal("ONE", invalidRow.GetProperty("code").GetString());
        Assert.Equal(1, invalidRow.GetProperty("row").GetInt32());
        Assert.Equal("1", invalidRow.GetProperty("value").GetString());
        Assert.Equal("invalid", invalidRow.GetProperty("reason").GetString());
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

    [Fact]
    public async Task Item_fields_are_a_delta_where_an_absent_key_keeps_its_value_and_null_clears_it()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Delta " + Guid.NewGuid().ToString("N"), en = "Delta" };
        object TextField(string key, int position) => new
        {
            localId = key, id = (Guid?)null, key, name = new { es = key, en = key }, type = "text", sourceCatalogId = (Guid?)null,
            required = false, unique = false, min = (string?)null, max = (string?)null, maxLength = 300, role = "data", position, isActive = true,
        };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts,
            fields = new[] { TextField("first", 0), TextField("second", 1) }, items = new[] { Item("one", null, "ONE", "One", null, true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var path = $"/api/forms/catalogs/{saved.GetProperty("catalog").GetProperty("id").GetGuid()}";
        var first = saved.GetProperty("fieldIdentityMap").GetProperty("first").GetGuid().ToString("D");
        var second = saved.GetProperty("fieldIdentityMap").GetProperty("second").GetGuid().ToString("D");
        var itemId = saved.GetProperty("itemIdentityMap").GetProperty("one").GetGuid();
        async Task<JsonElement> SaveAsync(long revision, Dictionary<string, string?> fields)
        {
            using var response = await client.PutAsJsonAsync(path, new { expectedRevision = revision, name = texts, description = texts,
                items = new[] { new { localId = "one", itemId, code = "ONE", text = new { es = "One", en = "One" }, isActive = true, fields } } }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var items = await client.GetAsync(path + "/items", Ct);
            return (await items.ReadJsonAsync()).GetProperty("items")[0].GetProperty("fields");
        }

        await SaveAsync(1, new() { [first] = "first", [second] = "second" });
        var omitted = await SaveAsync(2, new() { [first] = "changed" });
        Assert.Equal("changed", omitted.GetProperty(first).GetString());
        Assert.Equal("second", omitted.GetProperty(second).GetString());
        var cleared = await SaveAsync(3, new() { [second] = null });
        Assert.Equal("changed", cleared.GetProperty(first).GetString());
        Assert.False(cleared.TryGetProperty(second, out _));
    }

    [Fact]
    public async Task Converting_catalog_and_boolean_fields_to_text_keeps_their_visible_text()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        var texts = new { es = "Visible " + Guid.NewGuid().ToString("N"), en = "Visible" };
        using var sourceResponse = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts,
            items = new[] { Item("north", null, "N", "Norte", "North", true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, sourceResponse.StatusCode);
        var source = await sourceResponse.ReadJsonAsync();
        var sourceId = source.GetProperty("catalog").GetProperty("id").GetGuid();
        var northId = source.GetProperty("itemIdentityMap").GetProperty("north").GetGuid();
        object Field(string key, Guid? id, string type, Guid? sourceCatalogId, int position) => new
        {
            localId = id is null ? key : null, id, key, name = new { es = key, en = key }, type, sourceCatalogId,
            required = false, unique = false, min = (string?)null, max = (string?)null, maxLength = type == "text" ? 300 : (int?)null,
            role = "data", position, isActive = true,
        };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts,
            fields = new[] { Field("zone", null, "catalog", sourceId, 0), Field("approved", null, "boolean", null, 1) },
            items = new[] { Item("acme", null, "ACME", "Acme", null, true), Item("globex", null, "GLOBEX", "Globex", null, true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var path = $"/api/forms/catalogs/{saved.GetProperty("catalog").GetProperty("id").GetGuid()}";
        var zone = saved.GetProperty("fieldIdentityMap").GetProperty("zone").GetGuid();
        var approved = saved.GetProperty("fieldIdentityMap").GetProperty("approved").GetGuid();
        var acme = saved.GetProperty("itemIdentityMap").GetProperty("acme").GetGuid();
        var globex = saved.GetProperty("itemIdentityMap").GetProperty("globex").GetGuid();
        object Values(string localId, Guid itemId, string code, string text, string answer) => new
        {
            localId, itemId, code, text = new { es = text, en = text }, isActive = true,
            fields = new Dictionary<string, string?> { [zone.ToString("D")] = northId.ToString("D"), [approved.ToString("D")] = answer },
        };
        using var values = await client.PutAsJsonAsync(path, new { expectedRevision = 1, name = texts, description = texts,
            items = new[] { Values("acme", acme, "ACME", "Acme", "true"), Values("globex", globex, "GLOBEX", "Globex", "false") } }, Ct);
        Assert.Equal(HttpStatusCode.OK, values.StatusCode);
        using var deactivated = await client.PutAsJsonAsync($"/api/forms/catalogs/{sourceId}", new { expectedRevision = 1, name = texts, description = texts,
            items = new[] { Item("north", northId, "N", "Norte", "North", false) } }, Ct);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);

        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var converted = await client.PutAsJsonAsync(path, new { expectedRevision = 2, name = texts, description = texts,
            fields = new[] { Field("zone", zone, "text", null, 0), Field("approved", approved, "text", null, 1) }, items = Array.Empty<object>() }, Ct);

        Assert.Equal(HttpStatusCode.OK, converted.StatusCode);
        using var page = await client.GetAsync(path + "/items", Ct);
        var items = (await page.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetGuid(), item => item.GetProperty("fields"));
        Assert.Equal("Norte", items[acme].GetProperty(zone.ToString("D")).GetString());
        Assert.Equal("Sí", items[acme].GetProperty(approved.ToString("D")).GetString());
        Assert.Equal("Norte", items[globex].GetProperty(zone.ToString("D")).GetString());
        Assert.Equal("No", items[globex].GetProperty(approved.ToString("D")).GetString());
    }

    [Fact]
    public async Task Saving_or_importing_without_value_changes_keeps_option_audit_untouched()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Audit " + Guid.NewGuid().ToString("N"), en = "Audit" };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts,
            fields = new[] { FieldPayload(null, "text") }, items = new[] { Item("one", null, "ONE", "One", null, true), Item("two", null, "TWO", "Two", null, true) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var saved = await created.ReadJsonAsync();
        var id = saved.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = saved.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        var one = saved.GetProperty("itemIdentityMap").GetProperty("one").GetGuid();
        var two = saved.GetProperty("itemIdentityMap").GetProperty("two").GetGuid();
        var path = $"/api/forms/catalogs/{id}";
        object Save(long revision, object name, params object[] items) => new { expectedRevision = revision, name, description = texts, fields = new[] { FieldPayload(fieldId, "text") }, items };
        Task<Dictionary<Guid, DateTime?>> AuditAsync() => factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().AsNoTracking()
            .Where(item => item.CatalogId == id).ToDictionaryAsync(item => item.Id, item => item.ModifiedAtUtc, Ct));
        using var values = await client.PutAsJsonAsync(path, Save(1, texts, FieldItem("one", one, "ONE", fieldId, "Uno"), FieldItem("two", two, "TWO", fieldId, "Dos", "Two")), Ct);
        Assert.Equal(HttpStatusCode.OK, values.StatusCode);
        var before = await AuditAsync();

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var renamed = new { es = texts.es + " renamed", en = "Audit renamed" };
        using var rename = await client.PutAsJsonAsync(path, Save(2, renamed), Ct);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        Assert.Equal(before, await AuditAsync());

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var edited = await client.PutAsJsonAsync(path, Save(3, renamed, FieldItem("one", one, "ONE", fieldId, "Uno", "One edited")), Ct);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var afterEdit = await AuditAsync();
        Assert.True(afterEdit[one] > before[one]);
        Assert.Equal(before[two], afterEdit[two]);

        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var exported = await client.GetAsync(path + "/export", Ct);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        var workbook = await exported.Content.ReadAsByteArrayAsync(Ct);
        using var previewBody = CatalogImportContent(workbook, "export.xlsx", 4);
        using var preview = await client.PostAsync(path + "/import-previews", previewBody, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.ReadJsonAsync();
        Assert.Equal(2, plan.GetProperty("counts").GetProperty("unchanged").GetInt32());
        foreach (var key in new[] { "added", "changed", "deactivated", "reactivated", "errorRows" }) Assert.Equal(0, plan.GetProperty("counts").GetProperty(key).GetInt32());
        using var applyBody = CatalogImportContent(workbook, "export.xlsx", 4, sha: plan.GetProperty("sheetSha256").GetString());
        using var applied = await client.PostAsync(path + "/imports", applyBody, Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(afterEdit, await AuditAsync());
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
