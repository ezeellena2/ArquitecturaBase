using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
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
    public async Task Sealed_extra_fields_survive_catalog_edits_in_sheet_detail_and_visible_excel_columns(string culture)
    {
        using var client = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport]);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var catalog = BusinessFormCatalog.CreateManual(FieldReadText("Proveedores", "Suppliers"), new Dictionary<string, string>());
        var days = Guid.NewGuid(); var zone = Guid.NewGuid(); var retired = Guid.NewGuid();
        var fields = new[] { FieldReadConfig(days, "days", "integer", "data", 0, true),
            FieldReadConfig(zone, "zone", "text", "filter", 1, true), FieldReadConfig(retired, "retired", "text", "data", 2, false) };
        Assert.True(catalog.SetFields(fields, null).IsSuccess);
        var first = catalog.AddManualItem("ONE", "Primero", "First", null, null, 0).Value;
        var second = catalog.AddManualItem("TWO", "Segundo", "Second", null, null, 1).Value;
        await factory.ExecuteDbContextAsync(async db => { db.AddRange(catalog, first, second); await db.SaveChangesAsync(Ct); });
        var question = Field("multiChoice", false) with
        {
            Label = FieldReadText("Proveedores", "Suppliers"), Source = new(catalog.Id), Options = null, Selection = new(0, 3),
        };
        var seeded = await SeedSheetAsync(client, 0, question);
        var actor = Guid.Parse(client.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var recordIds = await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
            var now = DateTime.SpecifyKind(new DateTime(2026, 10, 23, 12, 0, 0), DateTimeKind.Utc);
            var ids = new List<Guid>();
            for (var index = 0; index < 3; index++)
            {
                var selected = index == 0 ? new[] { second.Id, first.Id } : index == 1 ? [first.Id] : Array.Empty<Guid>();
                var receipt = Guid.NewGuid(); var version = definition.ActiveVersionId!.Value;
                var record = BusinessFormRecord.Create(definition.Id, version, definition.AllocateRecordNumber(), actor, receipt, now,
                    JsonSerializer.Serialize(new[] { new { fieldId = question.FieldId, value = selected } }));
                db.Add(record); ids.Add(record.Id);
                db.Add(BusinessFormCaptureReceipt.Complete(receipt, definition.Id, version, "catalog-fields-read", record.Id, record.Number, now));
                foreach (var (item, position) in selected.Select((id, position) => (id, position)))
                {
                    db.Add(BusinessFormRecordValue.Create(record.Id, definition.Id, question.FieldId, "multiChoice", choiceId: item, position: position));
                    var snapshot = index != 0 ? null : JsonSerializer.Serialize(new[]
                    {
                        new FormCatalogCapturedField(days, "days", FieldReadText("Días históricos", "Historical days"), "integer", "data", JsonSerializer.SerializeToElement(item == first.Id ? 0 : 30)),
                        new FormCatalogCapturedField(zone, "zone", new Dictionary<string, string> { ["es"] = "Zona" }, "text", "filter", JsonSerializer.SerializeToElement(item == first.Id ? "Norte" : "Centro")),
                        new FormCatalogCapturedField(retired, "retired", FieldReadText("Campo retirado", "Retired field"), "text", "data", JsonSerializer.SerializeToElement("historical")),
                    }, JsonSerializerOptions.Web);
                    var label = item == first.Id ? "Primero histórico" : "Segundo histórico";
                    db.Add(BusinessFormRecordReference.Create(record.Id, definition.Id, question.FieldId, catalog.Id, 1, item,
                        label, null, "Proveedores", fieldValuesJson: snapshot));
                }
            }
            await db.SaveChangesAsync(Ct); return ids;
        });
        await factory.ExecuteDbContextAsync(async db =>
        {
            var current = await db.Set<BusinessFormCatalog>().SingleAsync(value => value.Id == catalog.Id, Ct);
            Assert.True(current.SetFields(fields.Select(field => field.Id == days ? field with { Name = FieldReadText("Plazo actual", "Current term") } : field).ToArray(), null).IsSuccess);
            var items = await db.Set<BusinessFormCatalogItem>().Where(item => item.CatalogId == catalog.Id).ToArrayAsync(Ct);
            foreach (var item in items) Assert.True(current.UpdateManualItem(item, item.Code, "Changed", "Changed", null, null, item.Position).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        var path = $"/api/forms/definitions/{seeded.Id}/records";
        using var sheet = await client.PostAsJsonAsync(path + "/search", new { sort = "number", pageSize = 20 }, Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode); await ResponseContractCapture.WriteAsync(sheet, "extra-fields-sheet-" + culture, Ct);
        var sheetBody = await sheet.ReadJsonAsync();
        var column = sheetBody.GetProperty("columns").EnumerateArray().Single(value => value.GetProperty("fieldId").GetGuid() == question.FieldId);
        Assert.Equal(3, column.GetProperty("fields").GetArrayLength());
        Assert.Contains(column.GetProperty("fields").EnumerateArray(), value => value.GetProperty("fieldId").GetGuid() == retired);
        var rows = sheetBody.GetProperty("records").GetProperty("items");
        var choice = rows[0].GetProperty("cells").EnumerateArray().Single(value => value.GetProperty("fieldId").GetGuid() == question.FieldId).GetProperty("value")[0];
        Assert.Equal("Segundo histórico", choice.GetProperty("label").GetString());
        Assert.Equal(culture == "es" ? "Días históricos" : "Historical days", choice.GetProperty("fields")[0].GetProperty("label").GetString());
        Assert.Equal("30", choice.GetProperty("fields")[0].GetProperty("value").GetString());
        using var detail = await client.GetAsync("/api/forms/records/" + recordIds[0], Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode); await ResponseContractCapture.WriteAsync(detail, "extra-fields-detail-" + culture, Ct);
        var detailChoice = (await detail.ReadJsonAsync()).GetProperty("values").EnumerateArray().Single(value => value.GetProperty("fieldId").GetGuid() == question.FieldId).GetProperty("value")[0];
        Assert.Equal(3, detailChoice.GetProperty("fields").GetArrayLength());
        using var legacy = await client.GetAsync("/api/forms/records/" + recordIds[1], Ct);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode); await ResponseContractCapture.WriteAsync(legacy, "extra-fields-legacy-" + culture, Ct);
        var legacyChoice = (await legacy.ReadJsonAsync()).GetProperty("values").EnumerateArray().Single(value => value.GetProperty("fieldId").GetGuid() == question.FieldId).GetProperty("value")[0];
        Assert.False(legacyChoice.TryGetProperty("fields", out _));
        using var export = await client.PostAsJsonAsync(path + "/export", new { filter = new { sort = "number" }, columns = new[] { question.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode); await ResponseContractCapture.WriteAsync(export, "extra-fields-export-" + culture, Ct, "xlsx");
        using var all = new XLWorkbook(new MemoryStream(await export.Content.ReadAsByteArrayAsync(Ct)));
        var worksheet = all.Worksheet(1); Assert.Equal(8, worksheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal("30; 0", worksheet.Cell(2, 6).GetString()); Assert.Equal("Centro; Norte", worksheet.Cell(2, 7).GetString());
        Assert.Equal("—", worksheet.Cell(3, 6).GetString()); Assert.True(worksheet.Cell(4, 6).IsEmpty());
        using var visible = await client.PostAsJsonAsync(path + "/export", new { filter = new { sort = "number" }, columns = Array.Empty<Guid>(), catalogFields = new[] { new { questionId = question.FieldId, fieldId = zone } } }, Ct);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode); await ResponseContractCapture.WriteAsync(visible, "extra-fields-visible-export-" + culture, Ct, "xlsx");
        using var selectedWorkbook = new XLWorkbook(new MemoryStream(await visible.Content.ReadAsByteArrayAsync(Ct)));
        Assert.Equal(5, selectedWorkbook.Worksheet(1).LastColumnUsed()!.ColumnNumber());
        Assert.Equal("Centro; Norte", selectedWorkbook.Worksheet(1).Cell(2, 5).GetString());
        foreach (var suffix in new[] { "/search", "/filter-counts/search" })
        {
            object body = suffix == "/search" ? new { sort = "field", sortFieldId = days } : new { filter = new { }, dimension = "choice", fieldId = days };
            using var invalid = await client.PostAsJsonAsync(path + suffix, body, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
    }

    private static BusinessFormCatalogField FieldReadConfig(Guid id, string key, string type, string role, int position, bool active) =>
        new(id, key, FieldReadText(key, key), type, null, false, false, null, null, type == "text" ? 300 : null, role, position, active);
    private static Dictionary<string, string> FieldReadText(string es, string en) => new() { ["es"] = es, ["en"] = en };
}
