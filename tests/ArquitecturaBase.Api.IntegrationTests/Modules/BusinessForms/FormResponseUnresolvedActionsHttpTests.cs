using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// F2, «Por resolver»: a response has actions to resolve while one of them failed or is in doubt waiting for a person. Every reader of
// the sheet sees the mark and can filter by it; only whoever manages rules and sees every response gets the count.
public sealed partial class FormResponseEndpointsTests
{
    [Fact]
    public async Task The_sheet_marks_filters_and_counts_the_responses_with_actions_to_resolve()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsCreate, BusinessFormsPermissions.RulesManage]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RecordsExport, BusinessFormsPermissions.RulesManage]);
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        var seeded = await SeedSheetAsync(author, 7);
        var path = "/api/forms/definitions/" + seeded.Id + "/records";
        var numbers = seeded.RecordIds.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index + 1);
        async Task<JsonElement> Search(HttpClient client, object body)
        {
            using var response = await client.PostAsJsonAsync(path + "/search", body, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.ReadJsonAsync();
        }
        static int? Count(JsonElement sheet) => sheet.TryGetProperty("unresolvedActionCount", out var count) ? count.GetInt32() : null;
        IEnumerable<int> Numbers(JsonElement sheet) => sheet.GetProperty("records").GetProperty("items").EnumerateArray()
            .Select(row => numbers[row.GetProperty("id").GetGuid()]);
        static IEnumerable<int> Marked(IEnumerable<int> numbers, JsonElement sheet) => numbers.Zip(sheet.GetProperty("records").GetProperty("items").EnumerateArray())
            .Where(pair => pair.Second.TryGetProperty("hasUnresolvedActions", out var flag) && flag.GetBoolean()).Select(pair => pair.First);

        // Without any action run in the form, there is nothing to count, not even for whoever manages rules.
        Assert.Null(Count(await Search(manager, new { sort = "number" })));
        await SeedUnresolvedActionsAsync(seeded.Id, seeded.RecordIds);

        // Two responses have actions to resolve: two failed actions count once, and the voided one does not count.
        var all = await Search(manager, new { sort = "number" });
        Assert.Equal(2, Count(all));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 7 }, Numbers(all));
        Assert.Equal(new[] { 1, 2 }, Marked(Numbers(all), all));
        using (var unresolved = await manager.PostAsJsonAsync(path + "/search", new { sort = "number", unresolvedActions = true }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, unresolved.StatusCode);
            var body = await unresolved.ReadJsonAsync();
            Assert.Equal(new[] { 1, 2 }, Numbers(body));
            Assert.Equal(2, Count(body));
            await ResponseContractCapture.WriteAsync(unresolved, "unresolved-sheet-es", Ct);
        }
        Assert.Equal(new[] { 3, 4, 5, 7 }, Numbers(await Search(manager, new { sort = "number", unresolvedActions = false })));
        // The count is the form's, whatever the bar filters.
        Assert.Equal(2, Count(await Search(manager, new { sort = "number", search = "no-such-text" })));

        // The filter serves whatever uses the sheet's filter: its counts and its Excel.
        using (var counts = await manager.PostAsJsonAsync(path + "/filter-counts/search", new { dimension = "version", filter = new { unresolvedActions = true } }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, counts.StatusCode);
            Assert.Equal(2, Assert.Single((await counts.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("count").GetInt32());
        }
        using (var exported = await manager.PostAsJsonAsync(path + "/export", new { filter = new { sort = "number", unresolvedActions = true } }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
            await using var stream = await exported.Content.ReadAsStreamAsync(Ct);
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            Assert.Equal(2, workbook.Worksheet(1).RowsUsed().Skip(1).Count());
        }

        // Whoever does not manage rules, or sees only their own responses, sees the mark and filters by it, without the count.
        var read = await Search(reader, new { sort = "number" });
        Assert.Null(Count(read));
        Assert.Equal(new[] { 1, 2 }, Marked(Numbers(read), read));
        var own = await Search(author, new { sort = "number", unresolvedActions = true });
        Assert.Null(Count(own));
        Assert.Equal(new[] { 1, 2 }, Numbers(own));
        Assert.Equal(new[] { 1, 2 }, Marked(Numbers(own), own));
    }

    // The filter also serves the navigation (↑ ↓ and the full screen) and the dashboard: they walk and count only the marked responses.
    [Fact]
    public async Task The_navigation_and_the_dashboard_take_only_the_responses_with_actions_to_resolve()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var seeded = await SeedSheetAsync(manager, 7);
        await SeedUnresolvedActionsAsync(seeded.Id, seeded.RecordIds);
        var filter = new { sort = "number", unresolvedActions = true };

        AssertRecordNavigation(await ReadRecordNavigationAsync(manager, seeded.Id, seeded.RecordIds[0], filter), seeded.RecordIds[0], null, seeded.RecordIds[1], 1, 2);
        AssertRecordNavigation(await ReadRecordNavigationAsync(manager, seeded.Id, seeded.RecordIds[1], filter), seeded.RecordIds[1], seeded.RecordIds[0], null, 2, 2);
        using (var unmarked = await manager.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/records/{seeded.RecordIds[2]}/navigation", filter, Ct))
            Assert.Equal(HttpStatusCode.NotFound, unmarked.StatusCode);

        foreach (var (unresolved, count) in new[] { (true, 2), (false, 4) })
        {
            using var dashboard = await manager.PostAsJsonAsync($"/api/forms/definitions/{seeded.Id}/dashboard/search",
                new { filter = new { unresolvedActions = unresolved }, period = "month" }, Ct);
            Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
            Assert.Equal(count, (await dashboard.ReadJsonAsync()).GetProperty("count").GetInt32());
        }
    }

    // A correction stands for the response it replaces: an action stopped in the replaced version neither marks nor counts the new
    // one, which has no actions to resolve (decided on 2026-11-17).
    [Fact]
    public async Task A_corrected_response_leaves_the_actions_to_resolve_of_its_replaced_version_behind()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var seeded = await SeedSheetAsync(manager, 2);
        var corrector = Guid.Parse(manager.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var correctionId = await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == seeded.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            var rules = BusinessFormRuleSet.Create(definition.Id, 1, "es", "[]", false, version.SchemaJson);
            db.Add(rules); await db.SaveChangesAsync(Ct);
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            var run = BusinessFormActionRun.Create(seeded.Id, seeded.RecordIds[0], rules.Id, Guid.NewGuid(), Guid.NewGuid(), "Received", "NotifyAuthor", 0, null, "{}", true, now);
            Assert.True(run.RecordOutcome(run.Reserve(now, TimeSpan.FromMinutes(1)).Value.Fence, now, FormActionRunState.Failed, "unknown", true).IsSuccess);
            db.Add(run);
            var original = await db.Set<BusinessFormRecord>().SingleAsync(record => record.Id == seeded.RecordIds[0], Ct);
            var receiptId = Guid.NewGuid();
            var correction = original.Correct(version.Id, receiptId, original.ValuesJson, null, null, corrector, "Dato equivocado").Value;
            db.Add(correction);
            db.Add(BusinessFormCaptureReceipt.Complete(receiptId, definition.Id, version.Id, "test", correction.Id, correction.Number, now));
            await db.SaveChangesAsync(Ct);
            return correction.Id;
        });
        var path = "/api/forms/definitions/" + seeded.Id + "/records/search";

        using (var sheet = await manager.PostAsJsonAsync(path, new { sort = "number" }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, sheet.StatusCode);
            var body = await sheet.ReadJsonAsync();
            Assert.Equal(0, body.GetProperty("unresolvedActionCount").GetInt32());
            var rows = body.GetProperty("records").GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(new[] { correctionId, seeded.RecordIds[1] }, rows.Select(row => row.GetProperty("id").GetGuid()));
            Assert.All(rows, row => Assert.False(row.TryGetProperty("hasUnresolvedActions", out _)));
        }
        using (var unresolved = await manager.PostAsJsonAsync(path, new { sort = "number", unresolvedActions = true }, Ct))
        {
            Assert.Equal(HttpStatusCode.OK, unresolved.StatusCode);
            Assert.Empty((await unresolved.ReadJsonAsync()).GetProperty("records").GetProperty("items").EnumerateArray());
        }
    }

    // Response 1: two failed actions. 2: in doubt, its author already told. 3: the issuance that asks its author («¿La emito?»).
    // 4: in doubt, the app still consults it on its own. 5: completed and waiting. 6: failed, but the response was voided. 7: none.
    private async Task SeedUnresolvedActionsAsync(Guid definitionId, IReadOnlyList<Guid> recordIds)
    {
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == definitionId, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            var rules = BusinessFormRuleSet.Create(definition.Id, 1, "es", "[]", false, version.SchemaJson);
            db.Add(rules); await db.SaveChangesAsync(Ct);
            var now = factory.Clock.GetUtcNow().UtcDateTime;
            BusinessFormActionRun Run(int number, string kind, Func<BusinessFormActionRun, long, Result>? outcome = null)
            {
                var run = BusinessFormActionRun.Create(definitionId, recordIds[number - 1], rules.Id, Guid.NewGuid(), Guid.NewGuid(), "Received", kind, 0, null, "{}", true, now);
                if (outcome is not null) Assert.True(outcome(run, run.Reserve(now, TimeSpan.FromMinutes(1)).Value.Fence).IsSuccess);
                db.Add(run); return run;
            }
            Run(1, "NotifyAuthor", (run, fence) => run.RecordOutcome(fence, now, FormActionRunState.Failed, FormActionErrors.EffectUnconfirmed.Code, true));
            Run(1, "GeneratePdf", (run, fence) => run.RecordOutcome(fence, now, FormActionRunState.Failed, "unknown", false));
            Run(2, "NotifyRole", (run, fence) => run.RecordOutcome(fence, now, FormActionRunState.Uncertain, FormActionErrors.EffectUnconfirmed.Code, false)).RecordStallNotice(now);
            Run(3, "IssueDocument", (run, fence) => run.RecordOutcome(fence, now, FormActionRunState.Uncertain, FormActionErrors.AwaitingConfirmation.Code, false));
            Run(4, "IssueDocument", (run, fence) => run.RecordUnconfirmed(fence, now, FormActionErrors.EffectUnconfirmed.Code));
            Run(5, "GeneratePdf", (run, fence) => run.RecordOutcome(fence, now, FormActionRunState.Completed, null, false));
            Run(5, "NotifyAuthor");
            Run(6, "NotifyAuthor", (run, fence) => run.RecordOutcome(fence, now, FormActionRunState.Failed, "unknown", true));
            var voided = await db.Set<BusinessFormRecord>().SingleAsync(record => record.Id == recordIds[5], Ct);
            Assert.True(voided.Void(voided.ActorUserId, "Duplicada", now).IsSuccess);
            await db.SaveChangesAsync(Ct);
        });
    }
}
