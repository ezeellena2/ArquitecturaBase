using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Theory]
    [InlineData("Received", "1")]
    [InlineData("PendingApproval", "2")]
    [InlineData("Approved", "3,5")]
    [InlineData("Rejected", "4")]
    public async Task State_filters_the_get_post_count_and_export_while_pdf_presence_is_confirmed_per_row(string state, string expected)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport]);
        var seeded = await SeedSheetAsync(reader, 5);
        await SeedSheetStatesAsync(seeded.Id, seeded.RecordIds);
        var numbers = expected.Split(',').Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        var path = "/api/forms/definitions/" + seeded.Id + "/records";
        using var query = await reader.GetAsync(path + "?sort=number&state=" + state, Ct);
        using var search = await reader.PostAsJsonAsync(path + "/search", new { sort = "number", state }, Ct);
        foreach (var response in new[] { query, search })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.ReadJsonAsync();
            Assert.True(body.GetProperty("hasApproval").GetBoolean());
            var records = body.GetProperty("records");
            Assert.Equal(numbers.Length, records.GetProperty("totalCount").GetInt32());
            Assert.Equal(numbers.Select(number => seeded.RecordIds[(int)number - 1]),
                records.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
            foreach (var row in records.GetProperty("items").EnumerateArray())
            {
                Assert.Equal(state, row.GetProperty("state").GetString());
                Assert.Equal(row.GetProperty("id").GetGuid() == seeded.RecordIds[2], row.GetProperty("hasPdf").GetBoolean());
                Assert.False(row.TryGetProperty("documentId", out _));
            }
        }
        await ResponseContractCapture.WriteAsync(search, "state-" + state.ToLowerInvariant() + "-d2-es", Ct);
        using var exported = await reader.PostAsJsonAsync(path + "/export", new { filter = new { sort = "number", state } }, Ct);
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        await using var stream = await exported.Content.ReadAsStreamAsync(Ct);
        using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
        Assert.Equal(numbers.Select(number => seeded.Prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture)),
            workbook.Worksheet(1).RowsUsed().Skip(1).Select(row => row.Cell(1).GetString()));
        await ResponseContractCapture.WriteAsync(exported, "export-state-" + state.ToLowerInvariant() + "-d2", Ct, "xlsx");
    }

    [Fact]
    public async Task Existing_facets_keep_state_when_they_ignore_only_their_own_dimension()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 5);
        await SeedSheetStatesAsync(seeded.Id, seeded.RecordIds);
        var path = "/api/forms/definitions/" + seeded.Id + "/records/filter-counts/search";
        using var actors = await reader.PostAsJsonAsync(path, new { dimension = "actor", filter = new { state = "Approved", actorIds = new[] { Guid.NewGuid() } } }, Ct);
        Assert.Equal(HttpStatusCode.OK, actors.StatusCode);
        Assert.Equal(2, Assert.Single((await actors.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("count").GetInt32());
        using var versions = await reader.PostAsJsonAsync(path, new { dimension = "version", filter = new { state = "Approved" } }, Ct);
        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        Assert.Equal(2, Assert.Single((await versions.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("count").GetInt32());
        using var choices = await reader.PostAsJsonAsync(path, new { dimension = "choice", fieldId = seeded.Fields[2].FieldId,
            filter = new { state = "Approved", conditions = new[] { new { fieldId = seeded.Fields[2].FieldId, @operator = "in", values = new[] { "false" } } } } }, Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var choiceCounts = (await choices.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("count").GetInt32());
        Assert.Equal(2, choiceCounts["true"]); Assert.Equal(0, choiceCounts["false"]);
        using var presence = await reader.PostAsJsonAsync(path, new { dimension = "presence", fieldId = seeded.Fields[0].FieldId,
            filter = new { state = "Approved", conditions = new[] { new { fieldId = seeded.Fields[0].FieldId, @operator = "unanswered", values = Array.Empty<string>() } } } }, Ct);
        Assert.Equal(HttpStatusCode.OK, presence.StatusCode);
        var presenceCounts = (await presence.ReadJsonAsync()).GetProperty("items").EnumerateArray().ToDictionary(item => item.GetProperty("id").GetString()!, item => item.GetProperty("count").GetInt32());
        Assert.Equal(2, presenceCounts["answered"]); Assert.Equal(0, presenceCounts["unanswered"]); Assert.Equal(0, presenceCounts["notAsked"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("approved")]
    [InlineData("All")]
    [InlineData("Approved,Rejected")]
    public async Task The_http_filter_rejects_unknown_or_multiple_states(string state)
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(reader, 1);
        using var response = await reader.PostAsJsonAsync("/api/forms/definitions/" + seeded.Id + "/records/search", new { state }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Pdf_presence_is_an_exists_in_the_page_query_without_per_row_queries()
    {
        using var actor = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(actor, 5);
        await SeedSheetStatesAsync(seeded.Id, seeded.RecordIds);
        await factory.ExecuteScopeAsync(async services =>
        {
            var recorder = new FormResponseCommandRecorder();
            await using var db = new TestDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>()).AddInterceptors(recorder).Options);
            var reader = new FormResponseReader(db, new FormRecordReader(db));
            var definition = await reader.FindDefinitionAsync(seeded.Id, Ct); Assert.NotNull(definition);
            recorder.Commands.Clear();
            await reader.ListAsync(definition, new() { PageSize = 1 }, Ct);
            var one = recorder.Commands.Count;
            recorder.Commands.Clear();
            var page = await reader.ListAsync(definition, new() { PageSize = 20 }, Ct);
            Assert.Equal(one, recorder.Commands.Count);
            var documents = Assert.Single(recorder.Commands, command => command.Sql.Contains("BusinessFormDocuments", StringComparison.Ordinal));
            Assert.Contains("EXISTS", documents.Sql, StringComparison.Ordinal);
            foreach (var row in page.Items)
                Assert.Equal(row.Record.Number == 3, JsonSerializer.SerializeToElement(row, JsonSerializerOptions.Web).GetProperty("hasPdf").GetBoolean());
        });
    }

    private async Task SeedSheetStatesAsync(Guid definitionId, IReadOnlyList<Guid> recordIds)
    {
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == definitionId, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            var approval = BusinessFormRuleSet.Create(definition.Id, 1, "es", "[]", true, version.SchemaJson);
            var disabled = BusinessFormRuleSet.Create(definition.Id, 2, "es", "[]", false, version.SchemaJson);
            db.AddRange(approval, disabled); await db.SaveChangesAsync(Ct); definition.SetActiveRuleSetId(disabled.Id);
            var rows = await db.Set<BusinessFormRecord>().Where(record => recordIds.Contains(record.Id)).OrderBy(record => record.Number).ToArrayAsync(Ct);
            foreach (var row in rows) db.Entry(row).Property(record => record.RuleSetId).CurrentValue = approval.Id;
            foreach (var row in rows.Skip(1)) Assert.True(row.MarkPendingApproval().IsSuccess);
            Assert.True(rows[2].CompleteApproval(true).IsSuccess); Assert.True(rows[3].CompleteApproval(false).IsSuccess); Assert.True(rows[4].CompleteApproval(true).IsSuccess);
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            var run = BusinessFormActionRun.Create(definitionId, rows[2].Id, approval.Id, Guid.NewGuid(), Guid.NewGuid(), "Approved", "GeneratePdf", 0, null, "{}", true, now);
            var reservation = run.Reserve(now, TimeSpan.FromMinutes(1)); Assert.True(reservation.IsSuccess);
            Assert.True(run.RecordOutcome(reservation.Value.Fence, now, FormActionRunState.Completed, null, false).IsSuccess);
            var file = StoredFile.Create(rows[2].ActorUserId, "receipt.pdf", "application/pdf", 1, new string('a', 64), "synthetic-sheet-pdf-" + run.Id.ToString("N"), null);
            db.AddRange(run, file); await db.SaveChangesAsync(Ct);
            db.Add(BusinessFormDocument.Create(definitionId, rows[2].Id, run.Id, file.Id, "es", null, 0)); await db.SaveChangesAsync(Ct);
        });
    }
}
