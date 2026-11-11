using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// The location question (UB1-A to UB4-A) with PostgreSQL: publishing it by WhatsApp is blocked, and in the app the point is loaded,
/// kept as three decimal rows, read in the sheet and the detail as one datum, spread over three number columns in the Excel and
/// brought back to correct the response.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormLocationHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Point = """{"latitude":-34.603722,"longitude":-58.381592,"accuracyMeters":15}""";

    [Fact]
    public async Task A_location_is_loaded_in_the_app_and_read_as_one_datum_everywhere()
    {
        await using var scenario = await FormRecipientScenario.CreateAsync(factory, Ct);
        var saved = await scenario.SaveDraftAsync(payload => payload["draft"]!["fields"]!.AsArray().Add(new JsonObject
        {
            ["localId"] = "place", ["type"] = "location", ["required"] = true,
            ["label"] = new JsonObject { ["es"] = "Dónde estás", ["en"] = "Where you are" },
        }), Ct);
        var placeId = saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[1].GetProperty("fieldId").GetGuid();
        Assert.Equal("location", saved.GetProperty("definition").GetProperty("draft").GetProperty("fields")[1].GetProperty("type").GetString());

        // W1-A: completed also by WhatsApp, the check does not stop at the question, which WhatsApp asks for before the Flow.
        using var manager = scenario.Client("es");
        using var withChannel = await manager.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/publication-checks", CheckPayload(scenario, appOnly: false), Ct);
        Assert.Equal(HttpStatusCode.OK, withChannel.StatusCode);
        Assert.DoesNotContain((await withChannel.ReadJsonAsync()).GetProperty("issues").EnumerateArray(),
            value => value.TryGetProperty("fieldId", out var fieldId) && fieldId.ValueKind == JsonValueKind.String && fieldId.GetGuid() == placeId);
        await scenario.SaveDraftAsync(payload => { Fix(payload); payload["draft"]!["appOnly"] = true; }, Ct);

        var versionId = await scenario.PublishForPreparationAsync(factory, Ct);
        using var first = scenario.Client("es", scenario.FirstId);
        using var entry = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}", Ct);
        Assert.Equal(HttpStatusCode.OK, entry.StatusCode);
        await FormEntryContractCapture.WriteAsync(entry, "entry-location-es", Ct);
        Assert.Equal("location", (await entry.ReadJsonAsync()).GetProperty("questions")[1].GetProperty("type").GetString());

        var supplier = new { fieldId = scenario.QuestionId, value = scenario.FirstItemId.ToString("D") };
        using var malformed = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, supplier, new { fieldId = placeId, value = """{"latitude":95,"longitude":0}""" }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var sent = await first.PostAsync($"/api/forms/entries/{scenario.DefinitionId}",
            Send(Guid.NewGuid(), versionId, supplier, new { fieldId = placeId, value = Point }), Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        var recordId = (await sent.ReadJsonAsync()).GetProperty("recordId").GetGuid();
        var rows = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<BusinessFormRecordValue>()
            .Where(value => value.RecordId == recordId && value.FieldId == placeId).OrderBy(value => value.Position)
            .Select(value => new { value.Position, value.DecimalValue, value.Type }).ToArrayAsync(Ct));
        Assert.Equal([0, 1, 2], rows.Select(row => row.Position));
        Assert.Equal([-34.603722m, -58.381592m, 15m], rows.Select(row => row.DecimalValue!.Value));
        Assert.All(rows, row => Assert.Equal("location", row.Type));

        var readerId = await scenario.AddPersonAsync("Laura Ríos", [BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport], Ct);
        using var reader = scenario.Client("es", readerId);
        using var sheet = await reader.GetAsync($"/api/forms/definitions/{scenario.DefinitionId}/records", Ct);
        Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
        await FormEntryContractCapture.WriteAsync(sheet, "sheet-location-es", Ct);
        var cell = (await sheet.ReadJsonAsync()).GetProperty("records").GetProperty("items")[0].GetProperty("cells").EnumerateArray()
            .Single(value => value.GetProperty("fieldId").GetGuid() == placeId);
        Assert.Equal("Answered", cell.GetProperty("state").GetString());
        AssertPoint(cell.GetProperty("value"));
        using var sorted = await reader.PostAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/search",
            JsonContent.Create(new { sort = "field", sortFieldId = placeId }), Ct);
        Assert.Equal(HttpStatusCode.BadRequest, sorted.StatusCode);
        using var answered = await reader.PostAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/search",
            JsonContent.Create(new { conditions = new[] { new { fieldId = placeId, @operator = "answered" } } }), Ct);
        Assert.Equal(1, (await answered.ReadJsonAsync()).GetProperty("records").GetProperty("totalCount").GetInt32());

        using var detail = await reader.GetAsync($"/api/forms/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        await FormEntryContractCapture.WriteAsync(detail, "detail-location-es", Ct);
        AssertPoint((await detail.ReadJsonAsync()).GetProperty("values").EnumerateArray()
            .Single(value => value.GetProperty("fieldId").GetGuid() == placeId).GetProperty("value"));

        using var exported = await reader.PostAsJsonAsync($"/api/forms/definitions/{scenario.DefinitionId}/records/export",
            new { filter = new { }, columns = (Guid[]?)null }, Ct);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        using (var workbook = new ClosedXML.Excel.XLWorkbook(await exported.Content.ReadAsStreamAsync(Ct)))
        {
            var table = workbook.Worksheet(1);
            var headers = table.Row(1).CellsUsed().Select(value => value.GetString()).ToList();
            var latitude = headers.IndexOf("Dónde estás · Latitud") + 1;
            Assert.Equal(["Dónde estás · Latitud", "Dónde estás · Longitud", "Dónde estás · Precisión (m)"], headers.Skip(latitude - 1).Take(3));
            Assert.Equal(-34.603722, table.Cell(2, latitude).GetDouble(), 6);
            Assert.Equal(-58.381592, table.Cell(2, latitude + 1).GetDouble(), 6);
            Assert.Equal(15, table.Cell(2, latitude + 2).GetDouble());
        }

        using var correction = await first.GetAsync($"/api/forms/entries/{scenario.DefinitionId}/records/{recordId}", Ct);
        Assert.Equal(HttpStatusCode.OK, correction.StatusCode);
        await FormEntryContractCapture.WriteAsync(correction, "correction-location-es", Ct);
        var kept = (await correction.ReadJsonAsync()).GetProperty("answers").EnumerateArray().Single(value => value.GetProperty("fieldId").GetGuid() == placeId);
        Assert.Equal(Point, kept.GetProperty("value").GetString());
    }

    private static void AssertPoint(JsonElement value)
    {
        Assert.Equal(-34.603722m, value.GetProperty("latitude").GetDecimal());
        Assert.Equal(-58.381592m, value.GetProperty("longitude").GetDecimal());
        Assert.Equal(15, value.GetProperty("accuracyMeters").GetInt32());
    }

    // The saved location comes back with its identity and key: the draft and the check name it by a local id instead.
    private static void Fix(JsonObject payload)
    {
        var place = payload["draft"]!["fields"]![1]!.AsObject();
        place["localId"] = "place"; place.Remove("key");
    }

    private static JsonObject CheckPayload(FormRecipientScenario scenario, bool appOnly)
    {
        var payload = scenario.CheckPayload();
        Fix(payload);
        if (appOnly) payload["draft"]!["appOnly"] = true;
        return payload;
    }

    private static MultipartFormDataContent Send(Guid requestId, Guid versionId, params object[] answers) => new()
    {
        { new StringContent(requestId.ToString("D")), "requestId" },
        { new StringContent(versionId.ToString("D")), "versionId" },
        { new StringContent(JsonSerializer.Serialize(answers, JsonSerializerOptions.Web)), "answers" },
    };
}
