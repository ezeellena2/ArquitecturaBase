using System.Net;
using System.Net.Http.Json;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormCatalogEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Import_source_fields_export_their_exact_identity_and_localized_label_and_no_change_receipts(string culture)
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var texts = new { es = "Importación fuente " + Guid.NewGuid().ToString("N"), en = "Source import" };
        using var sourceResponse = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = texts, description = texts,
            items = new[] { Item("first", null, "CENTRAL", "Centro", "Central", true), Item("second", null, "SOUTH", "Sur", null, true) },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, sourceResponse.StatusCode);
        var source = await sourceResponse.ReadJsonAsync();
        var sourceId = source.GetProperty("catalog").GetProperty("id").GetGuid();
        var firstId = source.GetProperty("itemIdentityMap").GetProperty("first").GetGuid();
        var secondId = source.GetProperty("itemIdentityMap").GetProperty("second").GetGuid();
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = texts, description = texts, fields = new[] { FieldPayload(null, "catalog", sourceId) },
            items = new[] { Item("existing", null, "A", "Existente", "Existente", true) },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalog = await created.ReadJsonAsync();
        var id = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = catalog.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        var itemId = catalog.GetProperty("itemIdentityMap").GetProperty("existing").GetGuid();
        var path = $"/api/forms/catalogs/{id}";
        using var initial = await client.PutAsJsonAsync(path, new
        {
            expectedRevision = 1, name = texts, description = texts, fields = new[] { FieldPayload(fieldId, "catalog", sourceId) },
            items = new[] { FieldItem("existing", itemId, "A", fieldId, firstId.ToString("D"), "Existente") },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var csv = Encoding.UTF8.GetBytes("Code,Text,field\nA,Existente,SOUTH\nB,Nueva,CENTRAL\n");
        using var previewBody = CatalogImportContent(csv, "sources.csv", 2, "Append");
        using var preview = await client.PostAsync(path + "/import-previews", previewBody, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.ReadJsonAsync();
        Assert.Empty(plan.GetProperty("issues").EnumerateArray());
        var added = Assert.Single(plan.GetProperty("added").EnumerateArray());
        var fieldKey = fieldId.ToString("D");
        Assert.Equal(firstId.ToString("D"), added.GetProperty("fields").GetProperty(fieldKey).GetString());
        Assert.Equal(culture == "en" ? "Central" : "Centro", added.GetProperty("fieldTexts").GetProperty(fieldKey).GetString());
        var changed = Assert.Single(Assert.Single(plan.GetProperty("changed").EnumerateArray()).GetProperty("changes").EnumerateArray());
        Assert.Equal(fieldKey, changed.GetProperty("column").GetString());
        Assert.Equal(firstId.ToString("D"), changed.GetProperty("before").GetString());
        Assert.Equal(secondId.ToString("D"), changed.GetProperty("after").GetString());
        Assert.Equal(culture == "en" ? "Central" : "Centro", changed.GetProperty("beforeText").GetString());
        Assert.Equal("Sur", changed.GetProperty("afterText").GetString());
        await FormCatalogImportContractCapture.WriteAsync(preview, culture, "extra-source-preview", Ct);
        var sha = plan.GetProperty("sheetSha256").GetString();
        using var applyBody = CatalogImportContent(csv, "sources.csv", 2, "Append", sha);
        using var applied = await client.PostAsync(path + "/imports", applyBody, Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var receipt = await applied.ReadJsonAsync();
        Assert.Equal(3, receipt.GetProperty("catalog").GetProperty("revision").GetInt64());
        Assert.Empty(receipt.GetProperty("itemIdentityMap").EnumerateObject());
        Assert.Empty(receipt.GetProperty("fieldIdentityMap").EnumerateObject());
        await FormCatalogImportContractCapture.WriteAsync(applied, culture, "extra-source-applied", Ct);
        using var unchangedBody = CatalogImportContent(csv, "sources.csv", 3, "Append", sha);
        using var unchanged = await client.PostAsync(path + "/imports", unchangedBody, Ct);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(3, (await unchanged.ReadJsonAsync()).GetProperty("catalog").GetProperty("revision").GetInt64());
        await FormCatalogImportContractCapture.WriteAsync(unchanged, culture, "extra-source-unchanged", Ct);
    }

    [Fact]
    public async Task Import_preview_reports_a_repeated_unique_value_with_accents_and_symbols_read_from_postgresql()
    {
        const string supplier = "Peñalba & Cía + Ñandú";
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        var texts = new { es = "Únicos " + Guid.NewGuid().ToString("N"), en = "Unique" };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = texts, description = texts, fields = new[] { FieldPayload(null, "text", unique: true) },
            items = new[] { Item("existing", null, "A", "Existente", "Existente", true) },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalog = await created.ReadJsonAsync();
        var id = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        var fieldId = catalog.GetProperty("fieldIdentityMap").GetProperty("field").GetGuid();
        var itemId = catalog.GetProperty("itemIdentityMap").GetProperty("existing").GetGuid();
        var path = $"/api/forms/catalogs/{id}";
        using var saved = await client.PutAsJsonAsync(path, new
        {
            expectedRevision = 1, name = texts, description = texts, fields = new[] { FieldPayload(fieldId, "text", unique: true) },
            items = new[] { FieldItem("existing", itemId, "A", fieldId, supplier, "Existente") },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        // jsonb returns the text without the escapes that the serializer writes for «ñ», accents, «&» and «+».
        var raw = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().Where(item => item.Id == itemId)
            .Select(item => item.FieldValuesJson).SingleAsync(Ct));
        Assert.Contains(supplier, raw, StringComparison.Ordinal);

        using var previewBody = CatalogImportContent(Encoding.UTF8.GetBytes($"Code,Text,field\nB,Nueva,{supplier}\n"), "unique.csv", 2, "Append");
        using var preview = await client.PostAsync(path + "/import-previews", previewBody, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var plan = await preview.ReadJsonAsync();
        Assert.Equal(1, plan.GetProperty("counts").GetProperty("errorRows").GetInt32());
        Assert.Empty(plan.GetProperty("added").EnumerateArray());
        var issue = Assert.Single(plan.GetProperty("issues").EnumerateArray());
        Assert.Equal(2, issue.GetProperty("row").GetInt32());
        Assert.Equal(fieldId.ToString("D"), issue.GetProperty("column").GetString());
        Assert.Equal("Forms.Catalog.FieldValueDuplicate", issue.GetProperty("code").GetString());
    }
}
