using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// Correcting and voiding responses with PostgreSQL (CW10-A and CW11-A): the correction replaces the response with the same
/// number through the common capture, the void keeps it with its number, and every read leaves out what is not current.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormRecordAmendmentHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Whoever_loaded_a_response_corrects_it_with_the_same_number_and_the_replaced_version_stays_in_its_history(string culture)
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        var readerId = await scenario.AddPersonAsync("Laura Ríos", [BusinessFormsPermissions.RecordsRead], Ct);
        using var first = scenario.Client(culture, scenario.FirstId);
        var original = await SubmitAsync(first, scenario, versionId);

        using var opened = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{original.RecordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, opened.StatusCode);
        await FormEntryContractCapture.WriteAsync(opened, "correction-" + culture, Ct);
        var correction = await opened.ReadJsonAsync();
        Assert.Equal(original.Number, correction.GetProperty("number").GetString());
        Assert.Equal(versionId, correction.GetProperty("entry").GetProperty("versionId").GetGuid());
        var answer = Assert.Single(correction.GetProperty("answers").EnumerateArray());
        Assert.Equal(scenario.FirstItemId.ToString("D"), answer.GetProperty("value").GetString());
        // The option chosen comes as the catalog presents it, in the language of the request.
        Assert.Equal(culture == "es" ? "Proveedor Centro" : "Central supplier", answer.GetProperty("options")[0].GetProperty("title").GetString());

        using var options = await first.GetAsync(
            $"/api/forms/entries/{scenario.DefinitionId}/records/{original.RecordId}/versions/{versionId}/questions/{scenario.QuestionId}/options", Ct);
        Assert.Equal(scenario.FirstItemId, (await options.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());

        using var saved = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{original.RecordId}",
            Correct(Guid.NewGuid(), versionId, "Era otro proveedor.", Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        if (culture == "es") await FormEntryContractCapture.WriteAsync(saved, "corrected-es", Ct);
        var corrected = await saved.ReadJsonAsync();
        Assert.Equal(original.Number, corrected.GetProperty("number").GetString());
        var correctedId = corrected.GetProperty("recordId").GetGuid();
        Assert.NotEqual(original.RecordId, correctedId);

        // Both are saved at the same instant of the test clock: each one is found by its id.
        var records = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecord>()
            .Where(record => record.DefinitionId == scenario.DefinitionId).ToArrayAsync(Ct));
        Assert.Equal(2, records.Length);
        var replacedRecord = records.Single(record => record.Id == original.RecordId);
        var correctionRecord = records.Single(record => record.Id == correctedId);
        Assert.Equal(correctedId, replacedRecord.ReplacedByRecordId);
        Assert.Equal((original.RecordId, scenario.FirstId, scenario.FirstId), (correctionRecord.ReplacesRecordId, correctionRecord.ActorUserId, correctionRecord.CorrectedByUserId));
        Assert.Equal(replacedRecord.Number, correctionRecord.Number);
        Assert.Equal(1, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormDefinition>()
            .Where(definition => definition.Id == scenario.DefinitionId).Select(definition => definition.LastRecordNumber).SingleAsync(Ct)));

        using var reader = scenario.Client(culture, readerId);
        using var sheet = await reader.GetAsync($"/api/forms/definitions/{scenario.DefinitionId}/records", Ct);
        if (culture == "es") await FormEntryContractCapture.WriteAsync(sheet, "sheet-corrected-es", Ct);
        var row = Assert.Single((await sheet.ReadJsonAsync()).GetProperty("records").GetProperty("items").EnumerateArray());
        Assert.Equal(correctedId, row.GetProperty("id").GetGuid());
        Assert.True(row.GetProperty("corrected").GetBoolean());
        using var own = await first.GetAsync($"/api/forms/records/{correctedId}", Ct);
        Assert.True((await own.ReadJsonAsync()).GetProperty("canAmend").GetBoolean());
        Assert.Equal("app", row.GetProperty("channel").GetString());

        using var detail = await reader.GetAsync($"/api/forms/records/{correctedId}", Ct);
        await FormEntryContractCapture.WriteAsync(detail, "detail-corrected-" + culture, Ct);
        var body = await detail.ReadJsonAsync();
        Assert.Equal(original.RecordId, body.GetProperty("correction").GetProperty("previousRecordId").GetGuid());
        Assert.Equal("Era otro proveedor.", body.GetProperty("correction").GetProperty("reason").GetString());
        Assert.Equal("Juan Gómez", body.GetProperty("correction").GetProperty("by").GetProperty("displayName").GetString());
        Assert.Equal("app", body.GetProperty("origin").GetProperty("channelId").GetString());
        Assert.False(body.TryGetProperty("canAmend", out _));

        using var previous = await first.GetAsync($"/api/forms/records/{original.RecordId}", Ct);
        if (culture == "es") await FormEntryContractCapture.WriteAsync(previous, "detail-replaced-es", Ct);
        var previousBody = await previous.ReadJsonAsync();
        Assert.Equal(correctedId, previousBody.GetProperty("replacedBy").GetProperty("recordId").GetGuid());
        Assert.False(previousBody.TryGetProperty("canAmend", out _));

        using var again = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{original.RecordId}",
            Correct(Guid.NewGuid(), versionId, "Otra vez", Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(FormRecordErrors.AmendUnavailable.Code, (await again.ReadJsonAsync()).GetProperty("code").GetString());

        using var overview = await first.GetAsync("/api/forms/overview", Ct);
        var item = (await overview.ReadJsonAsync()).GetProperty("forms").GetProperty("items").EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == scenario.DefinitionId);
        Assert.Equal(1, item.GetProperty("responseCount").GetInt64());
    }

    [Fact]
    public async Task With_the_permission_someone_else_corrects_a_response_with_the_catalog_filters_of_whoever_answered_it()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        var correctorId = await scenario.AddPersonAsync("Ana Pérez", [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsAmend], Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        var original = await SubmitAsync(first, scenario, versionId);
        using var corrector = scenario.Client("es", correctorId);

        using var options = await corrector.GetAsync(
            $"/api/forms/entries/{scenario.DefinitionId}/records/{original.RecordId}/versions/{versionId}/questions/{scenario.QuestionId}/options", Ct);
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        Assert.Equal(scenario.FirstItemId, Assert.Single((await options.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var saved = await corrector.PostAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{original.RecordId}",
            Correct(Guid.NewGuid(), versionId, "Dato equivocado", Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var correctedId = (await saved.ReadJsonAsync()).GetProperty("recordId").GetGuid();

        var corrected = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecord>()
            .SingleAsync(record => record.Id == correctedId, Ct));
        Assert.Equal((scenario.FirstId, correctorId), (corrected.ActorUserId, corrected.CorrectedByUserId));
        using var own = await first.GetAsync($"/api/forms/records/{correctedId}", Ct);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        using var second = scenario.Client("es", scenario.SecondId);
        using var foreign = await second.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{correctedId}", Ct);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task A_voided_response_keeps_its_number_and_leaves_the_sheet_the_counts_and_the_excel_unless_asked_for()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        var readerId = await scenario.AddPersonAsync("Laura Ríos", [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport], Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        var voided = await SubmitAsync(first, scenario, versionId);
        var kept = await SubmitAsync(first, scenario, versionId);

        using var blank = await first.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/{voided.RecordId}/void", new { reason = " " }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        using var done = await first.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/{voided.RecordId}/void",
            new { reason = "Cargada dos veces." }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, done.StatusCode);
        using var twice = await first.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/{voided.RecordId}/void",
            new { reason = "Otra vez" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);

        using var reader = scenario.Client("es", readerId);
        using var sheet = await reader.GetAsync($"/api/forms/definitions/{scenario.DefinitionId}/records", Ct);
        var rows = (await sheet.ReadJsonAsync()).GetProperty("records");
        Assert.Equal(kept.RecordId, Assert.Single(rows.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        using var filtered = await reader.PostAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/search", JsonContent.Create(new { state = "Voided" }), Ct);
        await FormEntryContractCapture.WriteAsync(filtered, "sheet-voided-es", Ct);
        var voidedRow = Assert.Single((await filtered.ReadJsonAsync()).GetProperty("records").GetProperty("items").EnumerateArray());
        Assert.Equal(("Voided", voided.Number), (voidedRow.GetProperty("state").GetString(), voidedRow.GetProperty("number").GetString()));

        using var detail = await reader.GetAsync($"/api/forms/records/{voided.RecordId}", Ct);
        await FormEntryContractCapture.WriteAsync(detail, "detail-voided-es", Ct);
        var body = await detail.ReadJsonAsync();
        Assert.Equal("Cargada dos veces.", body.GetProperty("void").GetProperty("reason").GetString());
        Assert.Equal("Juan Gómez", body.GetProperty("void").GetProperty("by").GetProperty("displayName").GetString());

        Assert.Equal(1, await ExportedRowsAsync(reader, scenario.DefinitionId, includeVoided: false));
        Assert.Equal(2, await ExportedRowsAsync(reader, scenario.DefinitionId, includeVoided: true));

        using var overview = await first.GetAsync("/api/forms/overview", Ct);
        var item = (await overview.ReadJsonAsync()).GetProperty("forms").GetProperty("items").EnumerateArray().Single(value => value.GetProperty("id").GetGuid() == scenario.DefinitionId);
        Assert.Equal(1, item.GetProperty("responseCount").GetInt64());
        Assert.Equal(kept.Number, item.GetProperty("lastResponse").GetProperty("number").GetString());
    }

    [Fact]
    public async Task Only_viewing_responses_neither_corrects_nor_voids_them_and_a_decided_response_is_no_longer_its_authors_to_change()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        var viewerId = await scenario.AddPersonAsync("Laura Ríos", [BusinessFormsPermissions.RecordsRead], Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        var original = await SubmitAsync(first, scenario, versionId);
        using var viewer = scenario.Client("es", viewerId);

        using var opened = await viewer.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{original.RecordId}", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, opened.StatusCode);
        using var voided = await viewer.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/{original.RecordId}/void", new { reason = "No" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, voided.StatusCode);

        await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var record = await db.Set<BusinessFormRecord>().SingleAsync(value => value.Id == original.RecordId, Ct);
            Assert.True(record.MarkPendingApproval().IsSuccess);
            Assert.True(record.CompleteApproval(true).IsSuccess);
            return await db.SaveChangesAsync(Ct);
        });
        using var decided = await first.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/{original.RecordId}/void", new { reason = "Tarde" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, decided.StatusCode);
        using var detail = await first.GetAsync($"/api/forms/records/{original.RecordId}", Ct);
        Assert.False((await detail.ReadJsonAsync()).TryGetProperty("canAmend", out _));
    }

    private static async Task<int> ExportedRowsAsync(HttpClient client, Guid definitionId, bool includeVoided)
    {
        using var exported = await client.PostAsJsonAsync($"/api/forms/definitions/{definitionId}/records/export",
            new { filter = new { }, columns = (Guid[]?)null, includeVoided }, Ct);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        using var workbook = new ClosedXML.Excel.XLWorkbook(await exported.Content.ReadAsStreamAsync(Ct));
        return workbook.Worksheet(1).RowsUsed().Count() - 1;
    }

    private static async Task<(Guid RecordId, string Number)> SubmitAsync(HttpClient client, FormRecipientScenario scenario, Guid versionId)
    {
        using var sent = await client.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, Choice(scenario.QuestionId, scenario.FirstItemId)), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var body = await sent.ReadJsonAsync();
        return (body.GetProperty("recordId").GetGuid(), body.GetProperty("number").GetString()!);
    }

    private static object Choice(Guid fieldId, Guid itemId) => new { fieldId, value = itemId.ToString("D") };

    private static MultipartFormDataContent Send(Guid requestId, Guid versionId, params object[] answers) => new()
    {
        { new StringContent(requestId.ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };

    private static MultipartFormDataContent Correct(Guid requestId, Guid versionId, string reason, params object[] answers)
    {
        var content = Send(requestId, versionId, answers);
        content.Add(new StringContent(reason), "reason");
        return content;
    }
}
