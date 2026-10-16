using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
        await ResponseContractCapture.WriteAsync(listed, "filtered-es", Ct);
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
        await ResponseContractCapture.WriteAsync(facets, "counts-es", Ct);
        var options = (await facets.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("count").GetInt32());
        Assert.Equal(3, options["true"]); Assert.Equal(2, options["false"]);
        using var detail = await reader.GetAsync("/api/forms/records/" + seeded.RecordIds[1], Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await ResponseContractCapture.WriteAsync(detail, "detail-es", Ct);
        Assert.False((await detail.ReadJsonAsync()).GetProperty("values")[2].GetProperty("value").GetBoolean());
        foreach (var culture in new[] { "es", "en" })
        {
            reader.DefaultRequestHeaders.AcceptLanguage.Clear(); reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var sheet = await reader.GetAsync(path + "?pageSize=20", Ct);
            Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
            await ResponseContractCapture.WriteAsync(sheet, "sheet-" + culture, Ct);
            using var navigation = await reader.GetAsync("/api/forms/navigation?search=" + seeded.Prefix, Ct);
            Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
            await ResponseContractCapture.WriteAsync(navigation, "navigation-" + culture, Ct);
        }
        using var tied = await reader.GetAsync(path + "?sort=-submittedAtUtc&page=2&pageSize=3", Ct);
        Assert.Equal(HttpStatusCode.OK, tied.StatusCode);
        Assert.Equal(seeded.Prefix + "-000004", (await tied.ReadJsonAsync()).GetProperty("records").GetProperty("items")[0].GetProperty("number").GetString());
        using var excluded = await reader.PostAsJsonAsync(path + "/search", new { receivedToUtc = "2026-10-16T12:00:00Z" }, Ct);
        Assert.Equal(0, (await excluded.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());
        using var invalid = await reader.PostAsJsonAsync(path + "/search", new { conditions = new[] { new { fieldId = seeded.Fields[0].FieldId, @operator = "contains", values = new[] { "1" } } } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
    [Theory]
    [InlineData("number")]
    [InlineData("-number")]
    [InlineData("field")]
    public async Task An_unknown_sort_field_returns_a_validation_error_for_every_sort_order(string sort)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 0);
        using var response = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/search",
            new { sort, sortFieldId = Guid.NewGuid() }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Forms.Record.FilterInvalid", (await response.ReadJsonAsync()).GetProperty("code").GetString());
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
    [Fact]
    public async Task Excel_requires_export_permission_keeps_typed_cells_and_rejects_over_ten_thousand_records()
    {
        using var readOnly = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var exporter = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport]);
        var seeded = await SeedSheetAsync(exporter, 3);
        var path = $"/api/forms/definitions/{seeded.Id}/records/export";
        using var denied = await readOnly.PostAsJsonAsync(path, new { filter = new { } }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var response = await exporter.PostAsJsonAsync(path, new { filter = new { sort = "number" }, columns = new[] { seeded.Fields[0].FieldId, seeded.Fields[2].FieldId, seeded.Fields[3].FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        await ResponseContractCapture.WriteAsync(response, "export", Ct, "xlsx");
        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(7, sheet.LastColumnUsed()!.ColumnNumber());
        Assert.Equal(ClosedXML.Excel.XLDataType.Number, sheet.Cell(2, 5).DataType);
        Assert.Equal(0d, sheet.Cell(2, 5).GetDouble());
        Assert.True(sheet.Cell(2, 6).GetBoolean());
        Assert.Equal(ClosedXML.Excel.XLDataType.DateTime, sheet.Cell(2, 7).DataType);
        var large = await SeedSheetAsync(exporter, 10001);
        await ExplainSheetAsync(large.Id, large.Fields[0].FieldId);
        using var tooLarge = await exporter.PostAsJsonAsync($"/api/forms/definitions/{large.Id}/records/export", new { filter = new { } }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, tooLarge.StatusCode);
        Assert.Equal("Forms.Record.ExportTooLarge", (await tooLarge.ReadJsonAsync()).GetProperty("code").GetString());
    }
    [Fact]
    public async Task Excel_hydrates_two_hundred_and_fifty_responses_in_the_requested_order()
    {
        using var exporter = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport]);
        var seeded = await SeedSheetAsync(exporter, 250);
        using var response = await exporter.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/export", new { filter = new { sort = "number" }, columns = new[] { seeded.Fields[0].FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(251, sheet.LastRowUsed()!.RowNumber());
        Assert.Equal(seeded.Prefix + "-000001", sheet.Cell(2, 1).GetString());
        Assert.Equal(seeded.Prefix + "-000250", sheet.Cell(251, 1).GetString());
        Assert.Equal(249d, sheet.Cell(251, 5).GetDouble());
    }
    [Fact]
    public async Task Historical_questions_and_retired_choices_keep_their_identity_and_presence()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var retiredChoice = new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string,string> { ["es"] = "Retirada", ["en"] = "Retired" });
        var choice = Field("choice", false) with { Options = [retiredChoice] };
        var seeded = await SeedSheetAsync(reader, 2, choice);
        var removed = seeded.Fields[1];
        var added = Field("shortText", false);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
            var oldVersion = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == definition.ActiveVersionId, Ct);
            db.Set<BusinessFormField>().Add(BusinessFormField.Register(added.FieldId, definition.Id, added.Key, added.Type));
            await db.SaveChangesAsync(Ct);
            var replacement = BusinessFormVersion.Create(definition.Id, 2, "test-second-sheet");
            var current = seeded.Fields.Where(field => field.FieldId != removed.FieldId && field.FieldId != choice.FieldId).Prepend(added).Append(choice with { Options = [new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string,string> { ["es"] = "Nueva", ["en"] = "New" })] }).ToArray();
            replacement.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, current), SerializerOptions));
            replacement.SealPresentation(definition.NameJson, definition.SubmitLabelJson, "es", current.Length);
            db.Set<BusinessFormVersion>().Add(replacement);
            Assert.True(definition.ActivateVersion(oldVersion.Id, replacement.Id).IsSuccess);
            var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
            var receipt = Guid.NewGuid();
            var at = DateTime.SpecifyKind(new DateTime(2026, 10, 16, 13, 0, 0), DateTimeKind.Utc);
            var record = BusinessFormRecord.Create(definition.Id, replacement.Id, definition.AllocateRecordNumber(), actor, receipt, at, "[]");
            db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt, definition.Id, replacement.Id, "test", record.Id, record.Number, at));
            var firstRecord = await db.Set<BusinessFormRecord>().SingleAsync(value => value.Id == seeded.RecordIds[0], Ct);
            var captured = JsonSerializer.Deserialize<List<JsonElement>>(firstRecord.ValuesJson, SerializerOptions)!;
            captured.Add(JsonSerializer.SerializeToElement(new { fieldId = choice.FieldId, value = retiredChoice.ChoiceId }, SerializerOptions));
            db.Entry(firstRecord).Property(value => value.ValuesJson).CurrentValue = JsonSerializer.Serialize(captured, SerializerOptions);
            db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(seeded.RecordIds[0], definition.Id, choice.FieldId, "choice", choiceId: retiredChoice.ChoiceId));
            await db.SaveChangesAsync(Ct);
        });
        var path = $"/api/forms/definitions/{seeded.Id}/records";
        using var listed = await reader.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        var sheet = await listed.ReadJsonAsync();
        Assert.Equal(added.FieldId, sheet.GetProperty("columns")[0].GetProperty("fieldId").GetGuid());
        Assert.Equal(removed.FieldId, sheet.GetProperty("columns")[sheet.GetProperty("columns").GetArrayLength() - 1].GetProperty("fieldId").GetGuid());
        var rows = sheet.GetProperty("records").GetProperty("items");
        Assert.Equal("Unanswered", rows[0].GetProperty("cells")[0].GetProperty("state").GetString());
        Assert.Equal("NotAsked", rows[0].GetProperty("cells")[rows[0].GetProperty("cells").GetArrayLength() - 1].GetProperty("state").GetString());
        Assert.Equal("NotAsked", rows[1].GetProperty("cells")[0].GetProperty("state").GetString());
        using var historical = await reader.PostAsJsonAsync(path + "/search", new { conditions = new[] { new { fieldId = choice.FieldId, @operator = "in", values = new[] { retiredChoice.ChoiceId.ToString() } } } }, Ct);
        Assert.Equal(1, (await historical.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());
        using var choices = await reader.PostAsJsonAsync(path + "/filter-counts/search", new { filter = new { }, dimension = "choice", fieldId = choice.FieldId }, Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        Assert.Contains((await choices.ReadJsonAsync()).GetProperty("items").EnumerateArray(), option => option.GetProperty("id").GetString() == retiredChoice.ChoiceId.ToString() && option.GetProperty("count").GetInt32() == 1);
        using var presence = await reader.PostAsJsonAsync(path + "/filter-counts/search", new { filter = new { }, dimension = "presence", fieldId = added.FieldId }, Ct);
        Assert.Equal(HttpStatusCode.OK, presence.StatusCode);
        var counts = (await presence.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToDictionary(option => option.GetProperty("id").GetString()!, option => option.GetProperty("count").GetInt32());
        Assert.Equal(2, counts["notAsked"]); Assert.Equal(1, counts["unanswered"]); Assert.Equal(0, counts["answered"]);
    }
    [Fact]
    public async Task Revoking_record_read_removes_rows_counts_details_and_exports_on_the_next_request()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport]);
        var seeded = await SeedSheetAsync(reader, 1);
        using var warm = await reader.GetAsync($"/api/forms/definitions/{seeded.Id}/records", Ct);
        Assert.Equal(HttpStatusCode.OK, warm.StatusCode);
        var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        await factory.InTransactionAsync(services => services.GetRequiredService<ArquitecturaBase.Application.Interfaces.Persistence.IUserRepository>().SetRolesAsync(actor, [], Ct));
        foreach (var path in new[] { "/api/forms/navigation", $"/api/forms/definitions/{seeded.Id}/records", $"/api/forms/records/{seeded.RecordIds[0]}" })
        {
            using var denied = await reader.GetAsync(path, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.False((await denied.ReadJsonAsync()).TryGetProperty("items", out _));
        }
        foreach (var suffix in new[] { "search", "filter-counts/search", "export" })
        {
            using var denied = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/{suffix}", new { filter = new { } }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
    }
    private async Task ExplainSheetAsync(Guid definitionId, Guid fieldId)
    {
        await factory.ExecuteDbContextAsync(async db =>
        {
            var connection = db.Database.GetDbConnection();
            await db.Database.OpenConnectionAsync(Ct);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON)
                SELECT r."Id" FROM "BusinessFormRecords" r
                WHERE r."DefinitionId" = @definition
                  AND EXISTS (SELECT 1 FROM "BusinessFormRecordValues" v WHERE v."RecordId" = r."Id" AND v."FieldId" = @field AND v."DecimalValue" >= 100)
                ORDER BY r."Number" DESC LIMIT 20
                """;
            var definitionParameter = command.CreateParameter(); definitionParameter.ParameterName = "definition"; definitionParameter.Value = definitionId; command.Parameters.Add(definitionParameter);
            var fieldParameter = command.CreateParameter(); fieldParameter.ParameterName = "field"; fieldParameter.Value = fieldId; command.Parameters.Add(fieldParameter);
            var output = (string)(await command.ExecuteScalarAsync(Ct))!;
            using var document = JsonDocument.Parse(output);
            Assert.Equal(20d, document.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble());
            var destination = Environment.GetEnvironmentVariable("FORM_RESPONSES_HTTP_OUTPUT");
            if (!string.IsNullOrWhiteSpace(destination)) await File.WriteAllTextAsync(Path.Combine(destination, "explain.json"), output, Ct);
        });
    }
    private async Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetAsync(HttpClient reader, int count, CanonicalFormField? extra = null)
    {
        var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var prefix = "T" + Guid.NewGuid().ToString("N")[..8];
        var fields = new[] { Field("decimal", false), Field("longText", false), Field("boolean", false), Field("date", false) };
        if (extra is not null) fields = [..fields, extra];
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
