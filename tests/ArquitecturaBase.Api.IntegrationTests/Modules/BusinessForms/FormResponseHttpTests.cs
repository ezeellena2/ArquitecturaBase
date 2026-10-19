using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
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
    [InlineData("es", "en")]
    [InlineData("en", "es")]
    public async Task Ordering_and_facets_use_original_choice_labels_in_another_ui_language(string original, string secondary)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(secondary);
        var first = new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string, string> { [original] = "Zulu" });
        var second = new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string, string> { [original] = "Alpha" });
        var choice = Field("choice", false) with { Options = [first, second] };
        var seeded = await SeedSheetCoreAsync(reader, 3, null, [choice], original);
        await factory.ExecuteDbContextAsync(async db =>
        {
            for (var index = 0; index < seeded.RecordIds.Count; index++)
            {
                var id = seeded.RecordIds[index];
                var option = index == 1 ? second.ChoiceId : first.ChoiceId;
                var record = await db.Set<BusinessFormRecord>().SingleAsync(value => value.Id == id, Ct);
                var values = JsonSerializer.Deserialize<List<JsonElement>>(record.ValuesJson, SerializerOptions)!;
                values.Add(JsonSerializer.SerializeToElement(new { fieldId = choice.FieldId, value = option }, SerializerOptions));
                db.Entry(record).Property(value => value.ValuesJson).CurrentValue = JsonSerializer.Serialize(values, SerializerOptions);
                db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(id, seeded.Id, choice.FieldId, "choice", choiceId: option));
            }
            await db.SaveChangesAsync(Ct);
        });
        foreach (var sort in new[] { "field", "-field" })
        {
            using var response = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/search", new { sort, sortFieldId = choice.FieldId }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var expected = sort == "field" ? new[] { 2, 1, 3 } : new[] { 1, 3, 2 };
            Assert.Equal(expected.Select(number => seeded.Prefix + "-" + number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)),
                (await response.ReadJsonAsync()).GetProperty("records").GetProperty("items").EnumerateArray().Select(row => row.GetProperty("number").GetString()));
        }
        var expectedName = seeded.Prefix + (original == "en" ? " Sheet" : " Planilla");
        using var navigation = await reader.GetAsync("/api/forms/navigation?search=" + seeded.Prefix, Ct);
        Assert.Equal(HttpStatusCode.OK, navigation.StatusCode);
        Assert.Equal(expectedName, Assert.Single((await navigation.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        using var facets = await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/filter-counts/search",
            new { dimension = "choice", fieldId = choice.FieldId, filter = new { } }, Ct);
        Assert.Equal(HttpStatusCode.OK, facets.StatusCode);
        var options = (await facets.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(new[] { "Alpha", "Zulu" }, options.Select(option => option.GetProperty("label").GetString()));
        Assert.Equal(new[] { 1, 2 }, options.Select(option => option.GetProperty("count").GetInt32()));
        using var detail = await reader.GetAsync("/api/forms/records/" + seeded.RecordIds[0], Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(expectedName, (await detail.ReadJsonAsync()).GetProperty("name").GetString());
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
        await factory.ExecuteDbContextAsync(async db =>
        {
            var draft = BusinessFormDefinition.Create();
            var name = JsonSerializer.Serialize(new { es = seeded.Prefix + " Borrador", en = seeded.Prefix + " Draft" });
            Assert.True(draft.SetDraft(1, seeded.Prefix + " Borrador", name, seeded.Prefix + "D", "es",
                "{\"es\":\"Enviar\",\"en\":\"Submit\"}", JsonSerializer.Serialize(new FormQuestionSchema(1, []), SerializerOptions), 0, true).IsSuccess);
            db.Set<BusinessFormDefinition>().Add(draft);
            var published = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
            Assert.True(published.SetDraft(published.Revision, "Nombre sin publicar",
                "{\"es\":\"Nombre sin publicar\",\"en\":\"Unpublished name\"}", published.Prefix, published.Culture,
                published.SubmitLabelJson, published.SchemaJson, published.QuestionCount, false).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var response = await reader.GetAsync("/api/forms/navigation?search=" + seeded.Prefix + "&pageSize=1", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.ReadJsonAsync();
        Assert.Equal(1, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(seeded.Id, json.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(seeded.Prefix + " Sheet", json.GetProperty("items")[0].GetProperty("name").GetString());
    }
    [Fact]
    public async Task Excel_uses_English_headers_and_the_sealed_question_translation()
    {
        using var exporter = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport]);
        var translated = Field("shortText", false) with { Label = new Dictionary<string, string> { ["es"] = "Detalle", ["en"] = "Detail" } };
        var seeded = await SeedSheetAsync(exporter, 1, translated);
        exporter.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        using var response = await exporter.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/export",
            new { filter = new { }, columns = new[] { translated.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(Ct);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);
        Assert.Equal("Responses", sheet.Name);
        Assert.Equal(new[] { "Number", "Received (UTC)", "Submitted by", "Channel", "Detail" },
            sheet.Row(1).CellsUsed().Select(cell => cell.GetString()));
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
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, replacement, Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single()), ["es", "en"]));
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
        Assert.Equal(2, counts["notAsked"]); Assert.Equal(3, counts["unanswered"]); Assert.Equal(0, counts["answered"]);
    }
    [Fact]
    public async Task Facets_use_latest_labels_numeric_versions_and_only_ignore_their_own_dimension()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var option = new CanonicalFormChoice(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Anterior", ["en"] = "Previous" });
        var choice = Field("choice", false) with { Options = [option] };
        var seeded = await SeedSheetAsync(reader, 2, choice);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
            var previous = definition.ActiveVersionId;
            foreach (var number in new long[] { 2, 12 })
            {
                var version = BusinessFormVersion.Create(definition.Id, number, "test-facet-version");
                var fields = seeded.Fields.Select(field => field.FieldId == choice.FieldId ? field with
                {
                    Options = [option with { Label = new Dictionary<string, string> { ["es"] = "Actual", ["en"] = "Current" } }],
                } : field).ToArray();
                version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
                version.SealPresentation(definition.NameJson, definition.SubmitLabelJson, "es", fields.Length);
                db.Set<BusinessFormVersion>().Add(version);
                db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single()), ["es", "en"]));
                await db.SaveChangesAsync(Ct);
                Assert.True(definition.ActivateVersion(previous, version.Id).IsSuccess);
                previous = version.Id;
                var receipt = Guid.NewGuid();
                var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
                var at = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
                var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, receipt, at, "[]");
                db.Set<BusinessFormRecord>().Add(record);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt, definition.Id, version.Id, "test", record.Id, record.Number, at));
                await db.SaveChangesAsync(Ct);
            }
        });
        var path = $"/api/forms/definitions/{seeded.Id}/records/filter-counts/search";
        using var choices = await reader.PostAsJsonAsync(path, new { dimension = "choice", fieldId = choice.FieldId, filter = new { } }, Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        Assert.Equal("Actual", Assert.Single((await choices.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("label").GetString());
        foreach (var dimension in new[] { "actor", "version" })
        {
            using var response = await reader.PostAsJsonAsync(path, new { dimension, fieldId = seeded.Fields[0].FieldId,
                filter = new { conditions = new[] { new { fieldId = seeded.Fields[0].FieldId, @operator = "gte", values = new[] { "1" } } } } }, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = (await response.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(1, items.Sum(item => item.GetProperty("count").GetInt32()));
            if (dimension == "version") Assert.Equal(new[] { "1", "2", "12" }, items.Select(item => item.GetProperty("label").GetString()));
        }
    }
    [Fact]
    public async Task A_new_version_captured_after_the_definition_read_is_excluded_from_that_snapshot()
    {
        using var actorClient = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(actorClient, 1);
        await factory.ExecuteScopeAsync(async services =>
        {
            var reader = services.GetRequiredService<IFormResponseReader>();
            var snapshot = await reader.FindDefinitionAsync(seeded.Id, Ct);
            Assert.NotNull(snapshot);
            await factory.ExecuteDbContextAsync(async db =>
            {
                var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
                var version = BusinessFormVersion.Create(definition.Id, 2, "test-concurrent-snapshot");
                version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, seeded.Fields), SerializerOptions));
                version.SealPresentation(definition.NameJson, definition.SubmitLabelJson, definition.Culture, seeded.Fields.Length);
                db.Set<BusinessFormVersion>().Add(version);
                db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single()), ["es", "en"]));
                await db.SaveChangesAsync(Ct);
                Assert.True(definition.ActivateVersion(definition.ActiveVersionId, version.Id).IsSuccess);
                var actor = Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
                var receipt = Guid.NewGuid();
                var at = new DateTime(2026, 10, 16, 13, 0, 0, DateTimeKind.Utc);
                var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, receipt, at, "[]");
                db.Set<BusinessFormRecord>().Add(record);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt, definition.Id, version.Id, "test", record.Id, record.Number, at));
                await db.SaveChangesAsync(Ct);
            });
            var page = await reader.ListAsync(snapshot, new(), Ct);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(seeded.RecordIds[0], Assert.Single(page.Items).Record.RecordId);
            var exportIds = await reader.ListExportIdsAsync(snapshot, new(), 10001, Ct);
            Assert.Single(exportIds);
            var refreshed = await reader.FindDefinitionAsync(seeded.Id, Ct);
            Assert.NotNull(refreshed);
            Assert.Equal(2, (await reader.ListAsync(refreshed, new(), Ct)).TotalCount);
        });
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
    [Fact]
    public async Task List_and_export_reuse_the_definition_versions_while_chat_loads_distinct_versions_once()
    {
        using var actorClient=await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded=await SeedSheetAsync(actorClient,3);
        await factory.ExecuteDbContextAsync(async db=>
        {
            var definition=await db.Set<BusinessFormDefinition>().SingleAsync(value=>value.Id==seeded.Id,Ct);
            var old=await db.Set<BusinessFormVersion>().SingleAsync(value=>value.Id==definition.ActiveVersionId,Ct);
            var next=BusinessFormVersion.Create(definition.Id,2,"test-light-records");
            next.SealSchema(old.SchemaJson);next.SealPresentation(old.NameJson!,old.SubmitLabelJson!,old.Culture!,seeded.Fields.Length);
            db.Set<BusinessFormVersion>().Add(next);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, next, Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single()), ["es", "en"]));
            await db.SaveChangesAsync(Ct);
            Assert.True(definition.ActivateVersion(old.Id,next.Id).IsSuccess);
            var previous=await db.Set<BusinessFormRecord>().Where(record=>record.DefinitionId==definition.Id).OrderBy(record=>record.Number).Take(2).ToArrayAsync(Ct);
            Assert.True(previous[0].MarkPendingApproval().IsSuccess);Assert.True(previous[0].CompleteApproval(true).IsSuccess);
            Assert.True(previous[1].MarkPendingApproval().IsSuccess);Assert.True(previous[1].CompleteApproval(false).IsSuccess);
            var actor=Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
            var receipt=Guid.NewGuid();var now=new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc);
            var record=BusinessFormRecord.Create(definition.Id,next.Id,definition.AllocateRecordNumber(),actor,receipt,now,"[]");
            db.Set<BusinessFormRecord>().Add(record);db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt,definition.Id,next.Id,"test-light-records",record.Id,record.Number,now));
            await db.SaveChangesAsync(Ct);
        });
        await factory.ExecuteScopeAsync(async services=>
        {
            var recorder=new FormResponseCommandRecorder();
            await using var db=new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
            var records=new FormRecordReader(db, services.GetRequiredService<IFormPublicationProtector>());var reader=new FormResponseReader(db,records);
            var definition=await reader.FindDefinitionAsync(seeded.Id,Ct);Assert.NotNull(definition);Assert.Equal(2,definition.Versions.Count);
            recorder.Commands.Clear();var page=await reader.ListAsync(definition,new(){PageSize=20},Ct);Assert.Equal(4,page.Items.Count);
            Assert.DoesNotContain(recorder.Commands,command=>command.Sql.Contains("NameJson",StringComparison.Ordinal)||command.Sql.Contains("SchemaJson",StringComparison.Ordinal));
            var receiptIds=page.Items.Select(row=>row.Record.SourceReceiptId).ToArray();
            recorder.Commands.Clear();var exported=await reader.ListByReceiptIdsAsync(definition,receiptIds,Ct);Assert.Equal(4,exported.Count);
            Assert.Equal("Approved",exported.Single(row=>row.Record.Number==1).State);Assert.Equal("Rejected",exported.Single(row=>row.Record.Number==2).State);
            Assert.DoesNotContain(recorder.Commands,command=>command.Sql.Contains("NameJson",StringComparison.Ordinal)||command.Sql.Contains("SchemaJson",StringComparison.Ordinal));
            recorder.Commands.Clear();var chat=(await records.FindBatchAsync(receiptIds,null,Ct)).Rows;Assert.Equal(4,chat.Count);
            var versions=Assert.Single(recorder.Commands,command=>command.Sql.Contains("NameJson",StringComparison.Ordinal)||command.Sql.Contains("SchemaJson",StringComparison.Ordinal));
            Assert.Contains("BusinessFormVersions",versions.Sql,StringComparison.Ordinal);Assert.Contains("BusinessFormPublicationOperations",versions.Sql,StringComparison.Ordinal);
            Assert.All(chat.GroupBy(row=>row.VersionId),group=>Assert.All(group,row=>
            {Assert.Same(group.First().NameJson,row.NameJson);Assert.Same(group.First().SchemaJson,row.SchemaJson);}));
        });
    }
    [Theory]
    [InlineData(" field ")]
    [InlineData(" -field ")]
    public async Task A_field_sort_with_spaces_without_a_question_is_a_bad_request(string sort)
    {
        using var reader=await OperatorAsync([BusinessFormsPermissions.RecordsRead]);var seeded=await SeedSheetAsync(reader,1);
        using var response=await reader.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/search",new{sort},Ct);
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Equal("Forms.Record.FilterInvalid",(await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private async Task ExplainSheetAsync(Guid definitionId, Guid fieldId)
    {
        await factory.ExecuteScopeAsync(async services =>
        {
            var recorder = new FormResponseCommandRecorder();
            await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(
                services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
            var reader = new FormResponseReader(db, new FormRecordReader(db, services.GetRequiredService<IFormPublicationProtector>()));
            var definition = await reader.FindDefinitionAsync(definitionId, Ct);
            Assert.NotNull(definition);
            recorder.Commands.Clear();
            var page = await reader.ListAsync(definition, new() { PageSize = 20, Sort = "-number",
                Conditions = [new(fieldId, "gte", ["100"])] }, Ct);
            Assert.Equal(20, page.Items.Count);
            var generated = Assert.Single(recorder.Commands, command => command.Sql.Contains("ORDER BY", StringComparison.Ordinal)
                && command.Sql.Contains("\"BusinessFormRecords\"", StringComparison.Ordinal) && command.Sql.Contains("LIMIT", StringComparison.Ordinal));
            await db.Database.OpenConnectionAsync(Ct);
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + generated.Sql;
            foreach (var parameter in generated.Parameters) command.Parameters.Add(parameter);
            var output = (string)(await command.ExecuteScalarAsync(Ct))!;
            using var document = JsonDocument.Parse(output);
            Assert.Equal(20d, document.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble());
            var destination = Environment.GetEnvironmentVariable("FORM_RESPONSES_HTTP_OUTPUT");
            if (!string.IsNullOrWhiteSpace(destination)) await File.WriteAllTextAsync(Path.Combine(destination, "explain.json"), output, Ct);
        });
    }
    [Fact]
    public async Task Response_sheet_seed_uses_a_prefix_accepted_by_the_form_editor()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.Read]);
        var seeded = await SeedSheetAsync(reader, 0, Guid.Parse("abcdefab-1234-4abc-8def-1234567890ab"));
        using var response = await reader.GetAsync($"/api/forms/definitions/{seeded.Id}", Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prefix = (await response.ReadJsonAsync()).GetProperty("definition").GetProperty("prefix").GetString();
        Assert.Matches("^[A-Z][A-Z0-9]{0,11}$", prefix!);
        Assert.Equal("TABCDEFAB", prefix);
    }

    private Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetAsync(HttpClient reader, int count, params CanonicalFormField[] extra) =>
        SeedSheetAsync(reader, count, null, extra);

    private Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetAsync(HttpClient reader, int count, Guid? prefixIdentity, params CanonicalFormField[] extra) =>
        SeedSheetCoreAsync(reader, count, prefixIdentity, extra);

    private async Task<(Guid Id, string Prefix, CanonicalFormField[] Fields, IReadOnlyList<Guid> RecordIds)> SeedSheetCoreAsync(HttpClient reader, int count, Guid? prefixIdentity, CanonicalFormField[] extra, string? onlyCulture = null, bool secondaryButtonMissing = false)
    {
        var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var prefix = "T" + (prefixIdentity ?? Guid.NewGuid()).ToString("N")[..8].ToUpperInvariant();
        var fields = new[] { Field("decimal", false), Field("longText", false), Field("boolean", false), Field("date", false) };
        if (extra.Length != 0) fields = [..fields, ..extra];
        var definition = BusinessFormDefinition.Create();
        var version = BusinessFormVersion.Create(definition.Id, 1, "test-sheet");
        var culture = onlyCulture ?? "es";
        var names = new Dictionary<string, string> { ["es"] = prefix + " Planilla", ["en"] = prefix + " Sheet" };
        var buttons = new Dictionary<string, string> { ["es"] = "Enviar", ["en"] = "Submit" };
        if (onlyCulture is not null && !secondaryButtonMissing)
        {
            names = names.Where(pair => pair.Key == culture).ToDictionary(pair => pair.Key, pair => pair.Value);
            buttons = buttons.Where(pair => pair.Key == culture).ToDictionary(pair => pair.Key, pair => pair.Value);
            fields = fields.Select(field => field with
            {
                Label = new Dictionary<string, string> { [culture] = field.Label[culture] }
            }).ToArray();
        }
        if (secondaryButtonMissing) buttons = new Dictionary<string, string> { [culture] = buttons[culture] };
        var name = JsonSerializer.Serialize(names);
        var submit = JsonSerializer.Serialize(buttons);
        Assert.True(definition.SetDraft(1, names[culture], name, prefix, culture, submit, JsonSerializer.Serialize(new FormQuestionSchema(1, fields), SerializerOptions), fields.Length, true).IsSuccess);
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, fields), SerializerOptions));
        version.SealPresentation(name, submit, culture, fields.Length);
        var ids = new List<Guid>();
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Set<BusinessFormDefinition>().Add(definition);
            foreach (var field in fields) db.Set<BusinessFormField>().Add(BusinessFormField.Register(field.FieldId, definition.Id, field.Key, field.Type));
            await db.SaveChangesAsync(Ct);
            db.Set<BusinessFormVersion>().Add(version);
            db.Set<BusinessFormPublicationOperation>().Add(FormPublishedVersionFixture.Create(factory, definition, version, actor, onlyCulture is null ? ["es", "en"] : [culture]));
            Assert.True(definition.ActivateVersion(null, version.Id).IsSuccess); await db.SaveChangesAsync(Ct);
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
