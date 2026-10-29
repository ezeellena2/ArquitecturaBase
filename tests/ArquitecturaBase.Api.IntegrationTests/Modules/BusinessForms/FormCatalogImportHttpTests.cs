using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormCatalogEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Import_preview_apply_and_export_round_trip_share_the_revision_and_preserve_inactive_items(string culture)
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var texts = new { es = "Importar " + Guid.NewGuid().ToString("N"), en = "Import " + Guid.NewGuid().ToString("N") };
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new { name = texts, description = texts,
            items = new[] { Item("a", null, "A", "Uno", "One", true), Item("b", null, "B", "Dos", "Two", true),
                Item("c", null, "C", "Tres", "Three", true), Item("d", null, "D", "Inactiva", "Inactive", false) } }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var csv = Encoding.UTF8.GetBytes("Code,Text,Active\nA,Uno cambiado,Yes\nNEW,Nueva,Yes\nBAD," + new string('x', 31) + ",Yes\n");
        using var previewBody = CatalogImportContent(csv, "catalog.csv", 1);
        using var preview = await client.PostAsync($"/api/forms/catalogs/{id}/import-previews", previewBody, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        await FormCatalogImportContractCapture.WriteAsync(preview, culture, "replace-preview", Ct);
        var plan = await preview.ReadJsonAsync();
        var counts = plan.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("added").GetInt32()); Assert.Equal(1, counts.GetProperty("changed").GetInt32());
        Assert.Equal(2, counts.GetProperty("deactivated").GetInt32()); Assert.Equal(1, counts.GetProperty("errorRows").GetInt32());
        var sha = plan.GetProperty("sheetSha256").GetString()!;
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().Where(value => value.Id == id).Select(value => value.Revision).SingleAsync(Ct)));
        Assert.Equal(4, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().CountAsync(value => value.CatalogId == id, Ct)));
        using var unconfirmedBody = CatalogImportContent(csv, "catalog.csv", 1, sha: sha);
        using var unconfirmed = await client.PostAsync($"/api/forms/catalogs/{id}/imports", unconfirmedBody, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        Assert.Equal("Forms.CatalogImport.ConfirmationRequired", (await unconfirmed.ReadJsonAsync()).GetProperty("code").GetString());
        await FormCatalogImportContractCapture.WriteAsync(unconfirmed, culture, "confirmation-required", Ct);
        using var applyBody = CatalogImportContent(csv, "catalog.csv", 1, sha: sha, deactivations: 2, errors: true);
        using var applied = await client.PostAsync($"/api/forms/catalogs/{id}/imports", applyBody, Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        Assert.Equal(2, (await applied.ReadJsonAsync()).GetProperty("catalog").GetProperty("revision").GetInt64());
        await FormCatalogImportContractCapture.WriteAsync(applied, culture, "applied", Ct);
        var marker = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalog>().Where(value => value.Id == id)
            .Select(value => new { value.LastImportSha256, value.LastImportAtUtc }).SingleAsync(Ct));
        Assert.Equal(sha, marker.LastImportSha256); Assert.NotNull(marker.LastImportAtUtc);
        Assert.Equal(5, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().CountAsync(value => value.CatalogId == id, Ct)));
        using var replayBody = CatalogImportContent(csv, "catalog.csv", 1, sha: sha, deactivations: 2, errors: true);
        using var replay = await client.PostAsync($"/api/forms/catalogs/{id}/imports", replayBody, Ct);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        await FormCatalogImportContractCapture.WriteAsync(replay, culture, "conflict", Ct);
        using var repeatedBody = CatalogImportContent(csv, "catalog.csv", 2);
        using var repeated = await client.PostAsync($"/api/forms/catalogs/{id}/import-previews", repeatedBody, Ct);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        Assert.Equal(marker.LastImportAtUtc, (await repeated.ReadJsonAsync()).GetProperty("alreadyAppliedAtUtc").GetDateTime());
        await FormCatalogImportContractCapture.WriteAsync(repeated, culture, "already-applied", Ct);
        using var appendBody = CatalogImportContent(Encoding.UTF8.GetBytes("Code,Text\nB,Dos\nSECOND,Segunda\n"), "append.csv", 2, "Append");
        using var append = await client.PostAsync($"/api/forms/catalogs/{id}/import-previews", appendBody, Ct);
        Assert.Equal(HttpStatusCode.OK, append.StatusCode);
        Assert.Equal(0, (await append.ReadJsonAsync()).GetProperty("counts").GetProperty("deactivated").GetInt32());
        Assert.Equal(1, (await append.ReadJsonAsync()).GetProperty("counts").GetProperty("reactivated").GetInt32());
        await FormCatalogImportContractCapture.WriteAsync(append, culture, "append-preview", Ct);
        using var exported = await client.GetAsync($"/api/forms/catalogs/{id}/export", Ct);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.True(exported.Headers.CacheControl!.NoStore); Assert.Equal("nosniff", Assert.Single(exported.Headers.GetValues("X-Content-Type-Options")));
        // The file carries the catalog's name, and with the menu in the original language there are no translation columns.
        var catalogName = culture == "es" ? texts.es : texts.en;
        Assert.Equal(catalogName + ".xlsx", exported.Content.Headers.ContentDisposition!.FileNameStar);
        await FormCatalogImportContractCapture.WriteAsync(exported, culture, "export", Ct, true);
        var workbookBytes = await exported.Content.ReadAsByteArrayAsync(Ct);
        using (var stream = new MemoryStream(workbookBytes))
        using (var workbook = new XLWorkbook(stream))
        {
            Assert.Equal(6, workbook.Worksheet(1).LastRowUsed()!.RowNumber());
            Assert.Equal(culture == "es" ? ["Código", "Texto", "Detalle", "Activa"] : ["Code", "Text", "Detail", "Active"],
                workbook.Worksheet(1).Row(1).CellsUsed().Select(cell => cell.GetString()));
        }
        using var roundTripBody = CatalogImportContent(workbookBytes, "export.xlsx", 2);
        using var roundTrip = await client.PostAsync($"/api/forms/catalogs/{id}/import-previews", roundTripBody, Ct);
        Assert.Equal(HttpStatusCode.OK, roundTrip.StatusCode);
        var unchanged = (await roundTrip.ReadJsonAsync()).GetProperty("counts");
        Assert.Equal(5, unchanged.GetProperty("unchanged").GetInt32());
        foreach (var key in new[] { "added", "changed", "deactivated", "reactivated", "errorRows" }) Assert.Equal(0, unchanged.GetProperty(key).GetInt32());
        await FormCatalogImportContractCapture.WriteAsync(roundTrip, culture, "round-trip-preview", Ct);
        using var template = await client.GetAsync($"/api/forms/catalogs/{id}/export?template=true", Ct);
        Assert.Equal(HttpStatusCode.OK, template.StatusCode);
        Assert.Equal(catalogName + (culture == "es" ? " - plantilla" : " - template") + ".xlsx", template.Content.Headers.ContentDisposition!.FileNameStar);
        await FormCatalogImportContractCapture.WriteAsync(template, culture, "template", Ct, true);
        using var emptyBody = CatalogImportContent(await template.Content.ReadAsByteArrayAsync(Ct), "template.xlsx", 2);
        using var empty = await client.PostAsync($"/api/forms/catalogs/{id}/import-previews", emptyBody, Ct);
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var emptyPlan = await empty.ReadJsonAsync(); Assert.Equal(0, emptyPlan.GetProperty("rowsRead").GetInt32());
        Assert.Empty(emptyPlan.GetProperty("issues").EnumerateArray());
        await FormCatalogImportContractCapture.WriteAsync(empty, culture, "empty-preview", Ct);
    }

    [Fact]
    public async Task English_original_catalog_import_preview_reports_the_Spanish_translation_column()
    {
        using var client = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var created = await client.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = new { en = "English original import " + Guid.NewGuid().ToString("N") },
            items = new object[]
            {
                new { localId = "north", code = "N", text = new { en = "North", es = "Norte" }, isActive = true },
                new { localId = "east", code = "E", text = new { en = "East" }, isActive = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        // A Spanish menu translates the English original: «Texto» stays English and the translation column is Spanish.
        client.DefaultRequestHeaders.AcceptLanguage.Clear();
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
        using var exported = await client.GetAsync($"/api/forms/catalogs/{id}/export", Ct);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        await FormCatalogImportContractCapture.WriteAsync(exported, "es", "en-original-export", Ct, true);
        using (var stream = new MemoryStream(await exported.Content.ReadAsByteArrayAsync(Ct)))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet(1);
            Assert.Equal(["Código", "Texto", "Texto en español", "Detalle", "Detalle en español", "Activa"],
                sheet.Row(1).Cells(1, 6).Select(cell => cell.GetString()));
            Assert.Equal(["N", "North", "Norte"], sheet.Row(2).Cells(1, 3).Select(cell => cell.GetString()));
            Assert.Equal(["E", "East", ""], sheet.Row(3).Cells(1, 3).Select(cell => cell.GetString()));
        }
        var csv = Encoding.UTF8.GetBytes("Código,Texto,Texto en español\nN,North,Norte cambiado\nE,East,Este\nW,West,Oeste\n");
        using var previewBody = CatalogImportContent(csv, "english.csv", 1, "Append");
        using var preview = await client.PostAsync($"/api/forms/catalogs/{id}/import-previews", previewBody, Ct);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        await FormCatalogImportContractCapture.WriteAsync(preview, "es", "en-original-preview", Ct);
        var plan = await preview.ReadJsonAsync();
        Assert.Empty(plan.GetProperty("issues").EnumerateArray());
        Assert.Equal("Oeste", Assert.Single(plan.GetProperty("added").EnumerateArray()).GetProperty("text").GetString());
        var changes = plan.GetProperty("changed").EnumerateArray()
            .Select(row => (Code: row.GetProperty("code").GetString(), Change: Assert.Single(row.GetProperty("changes").EnumerateArray()))).ToArray();
        Assert.Equal(["N", "E"], changes.Select(row => row.Code));
        Assert.All(changes, row => Assert.Equal("textEs", row.Change.GetProperty("column").GetString()));
        Assert.Equal(["Norte", null], changes.Select(row => row.Change.GetProperty("before").GetString()));
        Assert.Equal(["Norte cambiado", "Este"], changes.Select(row => row.Change.GetProperty("after").GetString()));
        using var applyBody = CatalogImportContent(csv, "english.csv", 1, "Append", plan.GetProperty("sheetSha256").GetString());
        using var applied = await client.PostAsync($"/api/forms/catalogs/{id}/imports", applyBody, Ct);
        Assert.Equal(HttpStatusCode.OK, applied.StatusCode);
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == id)
            .OrderBy(item => item.Code).Select(item => new { item.Code, item.TextEn, item.TextEs }).ToListAsync(Ct));
        Assert.Equal([("E", "East", "Este"), ("N", "North", "Norte cambiado"), ("W", "West", "Oeste")],
            stored.Select(item => (item.Code, item.TextEn, item.TextEs)));
    }

    [Fact]
    public async Task Import_routes_require_read_and_manage_while_export_needs_only_read_and_the_file_limit_is_enforced()
    {
        using var manager = await OperatorAsync(BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage);
        using var created = await manager.PostAsJsonAsync("/api/forms/catalogs", Payload(), Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.ReadJsonAsync()).GetProperty("catalog").GetProperty("id").GetGuid();
        var csv = Encoding.UTF8.GetBytes("Code,Text\nA,Uno\n");
        using var reader = await OperatorAsync(BusinessFormsPermissions.CatalogsRead);
        using var anonymous = factory.CreateClient();
        foreach (var suffix in new[] { "import-previews", "imports" })
        {
            using var anonBody = CatalogImportContent(csv, "input.csv", 1);
            using var unauthorized = await anonymous.PostAsync($"/api/forms/catalogs/{id}/{suffix}", anonBody, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
            using var readBody = CatalogImportContent(csv, "input.csv", 1);
            using var forbidden = await reader.PostAsync($"/api/forms/catalogs/{id}/{suffix}", readBody, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
        using var readExport = await reader.GetAsync($"/api/forms/catalogs/{id}/export?template=true", Ct);
        Assert.Equal(HttpStatusCode.OK, readExport.StatusCode);
        using var tooLargeBody = CatalogImportContent(new byte[FormCatalogImportLimits.MaximumFileBytes + 1], "large.csv", 1);
        using var tooLarge = await manager.PostAsync($"/api/forms/catalogs/{id}/import-previews", tooLargeBody, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Equal("Forms.CatalogImport.FileTooLarge", (await tooLarge.ReadJsonAsync()).GetProperty("code").GetString());
        using var missingBody = CatalogImportContent(csv, "missing.csv", 1);
        using var missing = await manager.PostAsync($"/api/forms/catalogs/{Guid.NewGuid()}/import-previews", missingBody, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private static MultipartFormDataContent CatalogImportContent(byte[] bytes, string filename, long revision, string mode = "Replace",
        string? sha = null, int? deactivations = null, bool errors = false)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(bytes), "file", filename);
        form.Add(new StringContent(revision.ToString(CultureInfo.InvariantCulture)), "expectedRevision");
        form.Add(new StringContent(mode), "mode");
        if (sha is not null) form.Add(new StringContent(sha), "sheetSha256");
        if (deactivations is not null) form.Add(new StringContent(deactivations.Value.ToString(CultureInfo.InvariantCulture)), "confirmDeactivations");
        form.Add(new StringContent(errors ? "true" : "false"), "confirmErrors");
        return form;
    }
}
