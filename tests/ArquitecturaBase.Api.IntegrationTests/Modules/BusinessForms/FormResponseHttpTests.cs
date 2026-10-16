using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Response_routes_require_record_read_instead_of_template_read()
    {
        using var anonymous = factory.CreateClient();
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read]);
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var id = Guid.NewGuid();
        foreach (var route in new[] { "/api/forms/navigation", $"/api/forms/definitions/{id}/records", $"/api/forms/records/{id}" })
        {
            using var missingSession = await anonymous.GetAsync(route, Ct); Assert.Equal(HttpStatusCode.Unauthorized, missingSession.StatusCode);
            using var denied = await designer.GetAsync(route, Ct); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var allowed = await reader.GetAsync(route, Ct); Assert.Equal(route.EndsWith("navigation", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.NotFound, allowed.StatusCode);
        }
        foreach (var route in new[] { $"/api/forms/definitions/{id}/records/search", $"/api/forms/definitions/{id}/records/filter-counts/search" })
        {
            using var denied = await designer.PostAsJsonAsync(route, new { }, Ct); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
    }
    [Fact]
    public async Task Response_filters_sort_and_count_in_postgres_before_paging()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 25);
        var path = $"/api/forms/definitions/{seeded.Id}/records";
        using var listed = await reader.PostAsJsonAsync(path + "/search", new { page = 2, pageSize = 3, sort = "field", sortFieldId = seeded.Fields[0].FieldId,
            conditions = new[] { new { fieldId = seeded.Fields[0].FieldId, @operator = "gte", values = new[] { "10" } } } }, Ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var json = await listed.ReadJsonAsync();
        Assert.Equal(15, json.GetProperty("records").GetProperty("totalCount").GetInt32());
        Assert.Equal(3, json.GetProperty("records").GetProperty("items").GetArrayLength());
        Assert.Equal("13", json.GetProperty("records").GetProperty("items")[0].GetProperty("cells")[0].GetProperty("value").GetString());
        using var searched = await reader.PostAsJsonAsync(path + "/search", new { search = "private 12" }, Ct);
        Assert.Equal(1, (await searched.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());
        using var facets = await reader.PostAsJsonAsync(path + "/filter-counts/search", new { dimension = "choice", fieldId = seeded.Fields[2].FieldId,
            filter = new { conditions = new[] { new { fieldId = seeded.Fields[2].FieldId, @operator = "in", values = new[] { "true" } },
                new { fieldId = seeded.Fields[0].FieldId, @operator = "gte", values = new[] { "20" } } } } }, Ct);
        Assert.Equal(HttpStatusCode.OK, facets.StatusCode);
        var options = (await facets.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("count").GetInt32());
        Assert.Equal(3, options["true"]); Assert.Equal(2, options["false"]);
        using var detail = await reader.GetAsync("/api/forms/records/" + seeded.RecordIds[1], Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.False((await detail.ReadJsonAsync()).GetProperty("values")[2].GetProperty("value").GetBoolean());
        using var invalid = await reader.PostAsJsonAsync(path + "/search", new { conditions = new[] { new { fieldId = seeded.Fields[0].FieldId, @operator = "contains", values = new[] { "1" } } } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [Fact]
    public async Task Response_navigation_only_lists_published_forms_and_uses_sealed_translations()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 0);
        using var response = await reader.GetAsync("/api/forms/navigation?search=" + seeded.Prefix + "&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(seeded.Id, json.GetProperty("items")[0].GetProperty("id").GetGuid());
    }
    private async Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetAsync(HttpClient reader, int count)
    {
        var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var prefix = "T" + Guid.NewGuid().ToString("N")[..8];
        var fields = new[] { Field("decimal", false), Field("longText", false), Field("boolean", false), Field("date", false) };
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "test-sheet");
        var name = JsonSerializer.Serialize(new { es = prefix + " Planilla", en = prefix + " Sheet" });
        Assert.True(definition.SetDraft(1, prefix + " Planilla", name, prefix, "es", "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions), fields.Length, true).IsSuccess);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
        version.SealPresentation(name, "{\"es\":\"Enviar\",\"en\":\"Submit\"}", "es", fields.Length);
        var ids = new List<Guid>();
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version); Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess); await db.SaveChangesAsync(Ct);
            var now = DateTime.SpecifyKind(new DateTime(2026, 10, 16, 12, 0, 0), DateTimeKind.Utc);
            for (var index = 0; index < count; index++)
            {
                var receiptId = Guid.NewGuid();
                var values = JsonSerializer.Serialize(new object[] { new { fieldId = fields[0].FieldId, value = index }, new { fieldId = fields[1].FieldId, value = "private " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) }, new { fieldId = fields[2].FieldId, value = index % 2 == 0 }, new { fieldId = fields[3].FieldId, value = "2026-10-16" } }, SerializerOptions);
                var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, receiptId, now, values);
                db.Set<BusinessFormRecord>().Add(record); ids.Add(record.Id);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receiptId, definition.Id, version.Id, "test", record.Id, record.Number, now));
                db.Set<BusinessFormRecordValue>().AddRange(BusinessFormRecordValue.Create(record.Id, definition.Id, fields[0].FieldId, "decimal", decimalValue: index),
                    BusinessFormRecordValue.Create(record.Id, definition.Id, fields[1].FieldId, "longText", textValue: "private " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    BusinessFormRecordValue.Create(record.Id, definition.Id, fields[2].FieldId, "boolean", booleanValue: index % 2 == 0),
                    BusinessFormRecordValue.Create(record.Id, definition.Id, fields[3].FieldId, "date", dateValue: new DateOnly(2026, 10, 16)));
            }
            await db.SaveChangesAsync(Ct);
        });
        return (definition.Id, prefix, fields, ids);
    }
}
