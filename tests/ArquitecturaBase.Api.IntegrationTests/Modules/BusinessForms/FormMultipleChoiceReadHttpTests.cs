using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Multiple_choices_keep_history_order_and_per_record_counts_across_sheet_detail_and_excel(string culture)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport,
            BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage, BusinessFormsPermissions.RulesManage]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var names = new { es = "Riesgos " + Guid.NewGuid().ToString("N"), en = "Risks" };
        using var created = await reader.PostAsJsonAsync("/api/forms/catalogs", new
        {
            name = names, description = names,
            items = new[]
            {
                new { localId = "cold", code = "COLD", text = new { es = "Frío histórico", en = "Historical cold" }, isActive = true },
                new { localId = "noise", code = "NOISE", text = new { es = "Ruido histórico", en = "Historical noise" }, isActive = true },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var catalog = await created.ReadJsonAsync();
        var catalogId = catalog.GetProperty("catalog").GetProperty("id").GetGuid();
        var coldItem = catalog.GetProperty("itemIdentityMap").GetProperty("cold").GetGuid();
        var noiseItem = catalog.GetProperty("itemIdentityMap").GetProperty("noise").GetGuid();
        var cold = Guid.NewGuid(); var noise = Guid.NewGuid();
        var fixedFieldId = Guid.NewGuid(); var catalogFieldId = Guid.NewGuid();
        var fixedField = new CanonicalFormField(fixedFieldId, "field_" + fixedFieldId.ToString("N"), "multiChoice",
            new Dictionary<string, string> { ["es"] = "Fallas", ["en"] = "Issues" }, null, false, null,
            [new(cold, new Dictionary<string, string> { ["es"] = "Frío", ["en"] = "Cold" }),
             new(noise, new Dictionary<string, string> { ["es"] = "Ruido", ["en"] = "Noise" })], Selection: new(0, 3));
        var catalogField = fixedField with { FieldId = catalogFieldId, Key = "field_" + catalogFieldId.ToString("N"), Options = null,
            Label = new Dictionary<string, string> { ["es"] = "Riesgos históricos", ["en"] = "Historical risks" }, Source = new(catalogId) };
        var seeded = await SeedSheetAsync(reader, 0, fixedField, catalogField);
        var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var recordIds = await SeedMultipleChoiceReadsAsync(seeded.Id, actor, fixedField, catalogField,
            cold, noise, catalogId, coldItem, noiseItem);

        // Change the current catalog after the stored references exist. The public read must remain historical.
        using var changed = await reader.PutAsJsonAsync("/api/forms/catalogs/" + catalogId, new
        {
            expectedRevision = 1, name = names, description = names,
            items = new[]
            {
                new { localId = "cold", itemId = coldItem, code = "COLD", text = new { es = "Nuevo frío", en = "New cold" }, isActive = false },
                new { localId = "noise", itemId = noiseItem, code = "NOISE", text = new { es = "Nuevo ruido", en = "New noise" }, isActive = false },
            },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var path = $"/api/forms/definitions/{seeded.Id}/records";
        using var sheet = await reader.PostAsJsonAsync(path + "/search", new { pageSize = 20, sort = "number" }, Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        await ResponseContractCapture.WriteAsync(sheet, "multi-sheet-" + culture, Ct);
        var sheetBody = await sheet.ReadJsonAsync();
        var rows = sheetBody.GetProperty("records").GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(4, sheetBody.GetProperty("records").GetProperty("totalCount").GetInt32());
        var answered = rows[0].GetProperty("cells").EnumerateArray().Single(cell => cell.GetProperty("fieldId").GetGuid() == fixedField.FieldId);
        Assert.Equal("Answered", answered.GetProperty("state").GetString());
        Assert.Equal(2, answered.GetProperty("value").GetArrayLength());
        foreach (var row in rows.Skip(2)) Assert.Equal("Unanswered", row.GetProperty("cells").EnumerateArray()
            .Single(cell => cell.GetProperty("fieldId").GetGuid() == fixedField.FieldId).GetProperty("state").GetString());
        using var detail = await reader.GetAsync("/api/forms/records/" + recordIds[0], Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await ResponseContractCapture.WriteAsync(detail, "multi-detail-" + culture, Ct);
        var historical = (await detail.ReadJsonAsync()).GetProperty("values").EnumerateArray()
            .Single(value => value.GetProperty("fieldId").GetGuid() == catalogField.FieldId).GetProperty("value");
        Assert.Equal(new[] { noiseItem, coldItem }, historical.EnumerateArray().Select(value => value.GetProperty("choiceId").GetGuid()));
        Assert.Equal(culture == "en" ? new[] { "Historical noise", "Historical cold" } : new[] { "Ruido histórico", "Frío histórico" },
            historical.EnumerateArray().Select(value => value.GetProperty("label").GetString()));
        using var filtered = await reader.PostAsJsonAsync(path + "/search", new
        {
            conditions = new[] { new { fieldId = fixedField.FieldId, @operator = "anyOf", values = new[] { cold, noise } } },
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Equal(2, (await filtered.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());
        using var facets = await reader.PostAsJsonAsync(path + "/filter-counts/search", new { filter = new { }, dimension = "choice", fieldId = fixedField.FieldId }, Ct);
        Assert.Equal(HttpStatusCode.OK, facets.StatusCode);
        await ResponseContractCapture.WriteAsync(facets, "multi-counts-" + culture, Ct);
        var counts = (await facets.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .ToDictionary(value => value.GetProperty("id").GetString()!, value => value.GetProperty("count").GetInt32());
        Assert.Equal(2, counts[cold.ToString("D")]); Assert.Equal(1, counts[noise.ToString("D")]);
        using var presence = await reader.PostAsJsonAsync(path + "/filter-counts/search", new { filter = new { }, dimension = "presence", fieldId = fixedField.FieldId }, Ct);
        Assert.Equal(HttpStatusCode.OK, presence.StatusCode);
        var presenceCounts = (await presence.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .ToDictionary(value => value.GetProperty("id").GetString()!, value => value.GetProperty("count").GetInt32());
        Assert.Equal(2, presenceCounts["answered"]); Assert.Equal(2, presenceCounts["unanswered"]);
        Assert.Equal(0, presenceCounts["notAsked"]);
        foreach (var sort in new[] { "field", "-field" })
        {
            using var invalid = await reader.PostAsJsonAsync(path + "/search", new { sort, sortFieldId = fixedField.FieldId }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal("Forms.Record.FilterInvalid", (await invalid.ReadJsonAsync()).GetProperty("code").GetString());
        }
        using var export = await reader.PostAsJsonAsync(path + "/export", new { filter = new { sort = "number" }, columns = new[] { fixedField.FieldId, catalogField.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        await ResponseContractCapture.WriteAsync(export, "multi-export-" + culture, Ct, "xlsx");
        using var workbook = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync(Ct)));
        var worksheet = workbook.Worksheet(1);
        Assert.Equal(culture == "en" ? "Noise; Cold" : "Ruido; Frío", worksheet.Cell(2, 5).GetString());
        Assert.Equal(culture == "en" ? "Historical noise; Historical cold" : "Ruido histórico; Frío histórico", worksheet.Cell(2, 6).GetString());
        Assert.True(worksheet.Cell(4, 5).IsEmpty()); Assert.True(worksheet.Cell(5, 5).IsEmpty());

        using var pdf = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/pdf-design/preview", new
        {
            design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Constancia", ["en"] = "Receipt" }, [fixedField.FieldId], false, null, true), culture,
        }, Ct);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        await FormPdfContractCapture.WriteAsync(pdf, "multi-preview-" + culture, Ct, "pdf");
    }

    private async Task<IReadOnlyList<Guid>> SeedMultipleChoiceReadsAsync(Guid definitionId, Guid actor,
        CanonicalFormField fixedField, CanonicalFormField catalogField, Guid cold, Guid noise, Guid catalogId, Guid coldItem, Guid noiseItem)
    {
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == definitionId, Ct);
            var versionId = definition.ActiveVersionId!.Value;
            var now = DateTime.SpecifyKind(new DateTime(2026, 10, 22, 12, 0, 0), DateTimeKind.Utc);
            var ids = new List<Guid>();
            for (var index = 0; index < 4; index++)
            {
                var fixedIds = index switch { 0 => new[] { noise, cold }, 1 => [cold], _ => [] };
                var catalogIds = index switch { 0 => new[] { noiseItem, coldItem }, 1 => [coldItem], _ => [] };
                var sourceId = Guid.NewGuid();
                var values = index == 3 ? "[]" : JsonSerializer.Serialize(new[]
                { new { fieldId = fixedField.FieldId, value = fixedIds }, new { fieldId = catalogField.FieldId, value = catalogIds } });
                var record = BusinessFormRecord.Create(definitionId, versionId, definition.AllocateRecordNumber(), actor, sourceId, now, values);
                db.Set<BusinessFormRecord>().Add(record); ids.Add(record.Id);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(sourceId, definitionId, versionId, "multi-read-fixture", record.Id, record.Number, now));
                foreach (var (id, position) in fixedIds.Select((id, position) => (id, position)))
                    db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, definitionId, fixedField.FieldId, "multiChoice", choiceId: id, position: position));
                foreach (var (id, position) in catalogIds.Select((id, position) => (id, position)))
                {
                    db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id, definitionId, catalogField.FieldId, "multiChoice", choiceId: id, position: position));
                    var es = id == coldItem ? "Frío histórico" : "Ruido histórico";
                    var en = id == coldItem ? "Historical cold" : "Historical noise";
                    var translations = JsonSerializer.Serialize(new Dictionary<string, object> { ["es"] = new { text = es, detail = (string?)null, catalogName = "Riesgos históricos" }, ["en"] = new { text = en, detail = (string?)null, catalogName = "Historical risks" } });
                    db.Set<BusinessFormRecordReference>().Add(BusinessFormRecordReference.Create(record.Id, definitionId, catalogField.FieldId, catalogId, 1, id, es, null, "Riesgos", translations));
                }
            }
            await db.SaveChangesAsync(Ct); return (IReadOnlyList<Guid>)ids;
        });
    }
}
