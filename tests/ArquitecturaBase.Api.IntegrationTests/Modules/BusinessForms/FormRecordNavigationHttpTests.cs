using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Record_navigation_finds_global_neighbors_inside_the_filtered_sheet(string culture)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        reader.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var seeded = await SeedSheetAsync(reader, 25);
        var filtered = new
        {
            page = 2,
            pageSize = 3,
            sort = "field",
            sortFieldId = seeded.Fields[0].FieldId,
            conditions = new[] { new { fieldId = seeded.Fields[0].FieldId, @operator = "gte", values = new[] { "10" } } },
        };

        var middle = await ReadRecordNavigationAsync(reader, seeded.Id, seeded.RecordIds[12], filtered,
            "record-navigation-middle-" + culture);
        AssertRecordNavigation(middle, seeded.RecordIds[12], seeded.RecordIds[11], seeded.RecordIds[13], 3, 15);

        var first = await ReadRecordNavigationAsync(reader, seeded.Id, seeded.RecordIds[10], filtered,
            "record-navigation-first-" + culture);
        AssertRecordNavigation(first, seeded.RecordIds[10], null, seeded.RecordIds[11], 1, 15);

        var last = await ReadRecordNavigationAsync(reader, seeded.Id, seeded.RecordIds[24], filtered,
            "record-navigation-last-" + culture);
        AssertRecordNavigation(last, seeded.RecordIds[24], seeded.RecordIds[23], null, 15, 15);

        var singleton = await ReadRecordNavigationAsync(reader, seeded.Id, seeded.RecordIds[12],
            new { page = 20, pageSize = 1, search = "private 12" }, "record-navigation-singleton-" + culture);
        AssertRecordNavigation(singleton, seeded.RecordIds[12], null, null, 1, 1);
    }

    [Theory]
    [InlineData("number", false)]
    [InlineData("-number", true)]
    [InlineData("submittedAtUtc", false)]
    [InlineData("-submittedAtUtc", false)]
    [InlineData("actor", false)]
    [InlineData("-actor", false)]
    public async Task Record_navigation_preserves_the_sheets_number_tiebreaker(string sort, bool descending)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 3);
        var expected = descending ? seeded.RecordIds.Reverse().ToArray() : seeded.RecordIds.ToArray();
        var middle = await ReadRecordNavigationAsync(reader, seeded.Id, expected[1], new { sort, page = 10, pageSize = 1 });

        AssertRecordNavigation(middle, expected[1], expected[0], expected[2], 2, 3);
    }

    [Theory]
    [InlineData("field", new[] { 1, 3, 4, 0, 2 })]
    [InlineData("-field", new[] { 0, 2, 4, 1, 3 })]
    public async Task Record_navigation_preserves_null_placement_and_number_ties(string sort, int[] order)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var optional = Field("shortText", false);
        var seeded = await SeedSheetAsync(reader, 0, optional);
        var actor = Guid.Parse(reader.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var ids = new List<Guid>();
        string?[] texts = [null, "A", null, "A", "Z"];
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.Id, Ct);
            var versionId = definition.ActiveVersionId!.Value;
            var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
            foreach (var text in texts)
            {
                var receipt = Guid.NewGuid();
                var values = JsonSerializer.Serialize(text is null ? Array.Empty<object>() :
                    [new { fieldId = optional.FieldId, value = text }], SerializerOptions);
                var record = BusinessFormRecord.Create(definition.Id, versionId, definition.AllocateRecordNumber(), actor, receipt, now, values);
                ids.Add(record.Id);
                db.Set<BusinessFormRecord>().Add(record);
                db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(receipt, definition.Id, versionId,
                    "test-navigation", record.Id, record.Number, now));
                if (text is not null) db.Set<BusinessFormRecordValue>().Add(BusinessFormRecordValue.Create(record.Id,
                    definition.Id, optional.FieldId, "shortText", textValue: text));
            }
            await db.SaveChangesAsync(Ct);
        });
        var expected = order.Select(index => ids[index]).ToArray();
        for (var index = 0; index < expected.Length; index++)
        {
            var navigation = await ReadRecordNavigationAsync(reader, seeded.Id, expected[index],
                new { sort, sortFieldId = optional.FieldId, page = 99, pageSize = 1 });
            AssertRecordNavigation(navigation, expected[index], index == 0 ? null : expected[index - 1],
                index == expected.Length - 1 ? null : expected[index + 1], index + 1, expected.Length);
        }
    }

    [Fact]
    public async Task Record_navigation_requires_record_read_without_template_or_approval_permission()
    {
        using var anonymous = factory.CreateClient();
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 1);
        var path = $"/api/forms/definitions/{seeded.Id}/records/{seeded.RecordIds[0]}/navigation";

        using var missingSession = await anonymous.PostAsJsonAsync(path, new { }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missingSession.StatusCode);
        foreach (var deniedClient in new[] { designer, decider })
        {
            using var denied = await deniedClient.PostAsJsonAsync(path, new { }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        var allowed = await ReadRecordNavigationAsync(reader, seeded.Id, seeded.RecordIds[0], new { });
        AssertRecordNavigation(allowed, seeded.RecordIds[0], null, null, 1, 1);
    }

    [Fact]
    public async Task Record_navigation_does_not_reveal_an_absent_excluded_or_foreign_target()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 1);
        var other = await SeedSheetAsync(reader, 1);
        foreach (var (definitionId, recordId, filter) in new (Guid DefinitionId, Guid RecordId, object Filter)[]
        {
            (seeded.Id, Guid.NewGuid(), new { }),
            (seeded.Id, other.RecordIds[0], new { }),
            (Guid.NewGuid(), seeded.RecordIds[0], new { }),
            (seeded.Id, seeded.RecordIds[0], new { search = "not present in this response" }),
        })
        {
            using var unavailable = await reader.PostAsJsonAsync(
                $"/api/forms/definitions/{definitionId}/records/{recordId}/navigation", filter, Ct);
            Assert.Equal(HttpStatusCode.NotFound, unavailable.StatusCode);
            Assert.Equal(FormRecordErrors.NotFound.Code, (await unavailable.ReadJsonAsync()).GetProperty("code").GetString());
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("field")]
    public async Task Record_navigation_validates_the_existing_search_contract(string sort)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 1);
        using var invalid = await reader.PostAsJsonAsync(
            $"/api/forms/definitions/{seeded.Id}/records/{seeded.RecordIds[0]}/navigation", new { sort }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task<JsonElement> ReadRecordNavigationAsync(HttpClient reader, Guid definitionId,
        Guid recordId, object filter, string? captureName = null)
    {
        using var response = await reader.PostAsJsonAsync(
            $"/api/forms/definitions/{definitionId}/records/{recordId}/navigation", filter, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        if (captureName is not null) await ResponseContractCapture.WriteAsync(response, captureName, Ct);
        return await response.ReadJsonAsync();
    }

    private static void AssertRecordNavigation(JsonElement response, Guid recordId, Guid? previousId,
        Guid? nextId, int position, int totalCount)
    {
        Assert.Equal(new[] { "nextId", "position", "previousId", "recordId", "totalCount" },
            response.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(recordId, response.GetProperty("recordId").GetGuid());
        Assert.Equal(previousId, response.GetProperty("previousId").ValueKind == JsonValueKind.Null ? (Guid?)null : response.GetProperty("previousId").GetGuid());
        Assert.Equal(nextId, response.GetProperty("nextId").ValueKind == JsonValueKind.Null ? (Guid?)null : response.GetProperty("nextId").GetGuid());
        Assert.Equal(position, response.GetProperty("position").GetInt32());
        Assert.Equal(totalCount, response.GetProperty("totalCount").GetInt32());
    }
}
