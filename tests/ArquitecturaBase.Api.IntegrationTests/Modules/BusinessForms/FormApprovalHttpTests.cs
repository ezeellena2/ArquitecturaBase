using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Approval_routes_require_decide_and_an_authenticated_session_without_record_read()
    {
        using var anonymous = factory.CreateClient();
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        foreach (var route in new[] { "/api/forms/approvals", "/api/forms/approvals/count", "/api/forms/approvals/options", "/api/forms/approvals/" + Guid.NewGuid() })
        {
            using var missing = await anonymous.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            using var denied = await reader.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var allowed = await decider.GetAsync(route, Ct);
            Assert.Equal(route.EndsWith("approvals", StringComparison.Ordinal) || route.EndsWith("count", StringComparison.Ordinal) || route.EndsWith("options", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.NotFound, allowed.StatusCode);
        }
        var path = "/api/forms/approvals/" + Guid.NewGuid() + "/decisions";
        using var missingDecision = await anonymous.PostAsJsonAsync(path, new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missingDecision.StatusCode);
        using var deniedDecision = await reader.PostAsJsonAsync(path, new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedDecision.StatusCode);
    }
    [Fact]
    public async Task The_inbox_counts_and_pages_only_current_roles_and_excludes_own_responses_in_for_me()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        using var other = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var first = await SeedApprovalAsync(author, decider, 13);
        var own = await SeedApprovalAsync(decider, decider, 11);
        await SeedApprovalAsync(author, other, 10);
        using var mine = await decider.GetAsync("/api/forms/approvals", Ct);
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);
        Assert.Equal(1, (await mine.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        Assert.Equal(first.TaskId, (await mine.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        await FormApprovalContractCapture.WriteAsync(mine, "inbox-es", Ct);
        using var all = await decider.GetAsync("/api/forms/approvals?onlyMine=false&pageSize=1", Ct);
        Assert.Equal(2, (await all.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        Assert.Equal(own.TaskId, (await all.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.False((await all.ReadJsonAsync()).GetProperty("items")[0].GetProperty("canDecide").GetBoolean());
        using var next = await decider.GetAsync("/api/forms/approvals?onlyMine=false&pageSize=1&page=2", Ct);
        Assert.Equal(first.TaskId, (await next.ReadJsonAsync()).GetProperty("items")[0].GetProperty("id").GetGuid());
        using var count = await decider.GetAsync("/api/forms/approvals/count?definitionId=" + first.DefinitionId, Ct);
        Assert.Equal(1, (await count.ReadJsonAsync()).GetProperty("count").GetInt32()); await FormApprovalContractCapture.WriteAsync(count, "count-es", Ct);
        using var options = await decider.GetAsync("/api/forms/approvals/options", Ct);
        Assert.Equal(first.DefinitionId, Assert.Single((await options.ReadJsonAsync()).GetProperty("forms").EnumerateArray()).GetProperty("id").GetGuid()); await FormApprovalContractCapture.WriteAsync(options, "options-es", Ct);
        using var task = await decider.GetAsync("/api/forms/approvals/" + first.TaskId, Ct);
        Assert.True((await task.ReadJsonAsync()).GetProperty("canDecide").GetBoolean()); await FormApprovalContractCapture.WriteAsync(task, "task-open-es", Ct);
        using var ownDecision = await decider.PostAsJsonAsync("/api/forms/approvals/" + own.TaskId + "/decisions", new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, ownDecision.StatusCode); await FormApprovalContractCapture.WriteAsync(ownDecision, "own-response-es", Ct);
        decider.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en"); using var english = await decider.GetAsync("/api/forms/approvals", Ct); await FormApprovalContractCapture.WriteAsync(english, "inbox-en", Ct);
        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().SetRolesAsync(first.DeciderId, [], Ct));
        using var revoked = await decider.PostAsJsonAsync("/api/forms/approvals/" + first.TaskId + "/decisions", new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }
    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public async Task A_decision_closes_task_and_response_and_uses_the_received_rules_even_after_a_new_revision(string decision)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var seeded = await SeedApprovalAsync(author, decider, 12);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == seeded.DefinitionId, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == definition.ActiveVersionId, Ct);
            var newer = BusinessFormRuleSet.Create(definition.Id, 2, "es", "[]", false, version.SchemaJson); db.Set<BusinessFormRuleSet>().Add(newer); await db.SaveChangesAsync(Ct); definition.SetActiveRuleSetId(newer.Id); await db.SaveChangesAsync(Ct);
        });
        var route = "/api/forms/approvals/" + seeded.TaskId;
        if (decision == "Rejected")
        {
            using var missingReason = await decider.PostAsJsonAsync(route + "/decisions", new { expectedRevision = 1, decision }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, missingReason.StatusCode);
            using var tooLong = await decider.PostAsJsonAsync(route + "/decisions", new { expectedRevision = 1, decision, reason = new string('a', 501) }, Ct);
            Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        }
        using var response = await decider.PostAsJsonAsync(route + "/decisions", new { expectedRevision = 1, decision, reason = decision == "Rejected" ? "Revisá la visita" : null }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.ReadJsonAsync()).GetProperty("revision").GetInt64()); await FormApprovalContractCapture.WriteAsync(response, "decision-" + decision.ToLowerInvariant() + "-es", Ct);
        using var repeat = await decider.PostAsJsonAsync(route + "/decisions", new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode); await FormApprovalContractCapture.WriteAsync(repeat, "conflict-" + decision.ToLowerInvariant() + "-es", Ct);
        using var detail = await decider.GetAsync(route, Ct);
        Assert.Equal(decision, (await detail.ReadJsonAsync()).GetProperty("state").GetString());
        Assert.False((await detail.ReadJsonAsync()).GetProperty("canDecide").GetBoolean()); await FormApprovalContractCapture.WriteAsync(detail, "task-" + decision.ToLowerInvariant() + "-es", Ct);
        using var workflow = await author.GetAsync("/api/forms/records/" + seeded.RecordId + "/workflow", Ct);
        Assert.Equal(HttpStatusCode.OK, workflow.StatusCode);
        Assert.Equal(seeded.DeciderId, (await workflow.ReadJsonAsync()).GetProperty("task").GetProperty("decidedBy").GetProperty("userId").GetGuid());
        Assert.Equal(decision == "Rejected" ? "Revisá la visita" : null, (await workflow.ReadJsonAsync()).GetProperty("task").GetProperty("reason").GetString());
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.Equal(decision, (await db.Set<BusinessFormRecord>().SingleAsync(value => value.Id == seeded.RecordId, Ct)).State.ToString());
            var run = Assert.Single(await db.Set<BusinessFormActionRun>().Where(value => value.RecordId == seeded.RecordId && value.Event == decision).ToArrayAsync(Ct));
            Assert.Equal(seeded.RuleSetId, run.RuleSetId);
        });
    }
    [Fact]
    public async Task Two_simultaneous_decisions_commit_only_one_event()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]); using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var seeded = await SeedApprovalAsync(author, decider, 12);
        var route = "/api/forms/approvals/" + seeded.TaskId + "/decisions";
        var results = await Task.WhenAll(decider.PostAsJsonAsync(route, new { expectedRevision = 1, decision = "Approved" }, Ct), decider.PostAsJsonAsync(route, new { expectedRevision = 1, decision = "Rejected", reason = "Revisá" }, Ct));
        try
        {
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, result => result.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormActionRun>().CountAsync(run => run.RecordId == seeded.RecordId && run.Event != "Received", Ct)));
        }
        finally { foreach (var result in results) result.Dispose(); }
    }
    [Fact]
    public async Task Keeping_decide_permission_in_another_role_does_not_allow_deciding_the_removed_assignment()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var seeded = await SeedApprovalAsync(author, decider, 12);
        await factory.InTransactionAsync(async services =>
        {
            var roleName = "approval-other-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, [BusinessFormsPermissions.ApprovalsDecide], Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(seeded.DeciderId, [roleName], Ct);
        });
        using var count = await decider.GetAsync("/api/forms/approvals/count", Ct);
        Assert.Equal(HttpStatusCode.OK, count.StatusCode);
        Assert.Equal(0, (await count.ReadJsonAsync()).GetProperty("count").GetInt32());
        using var decision = await decider.PostAsJsonAsync("/api/forms/approvals/" + seeded.TaskId + "/decisions", new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, decision.StatusCode);
        Assert.Equal(FormApprovalErrors.RoleChanged.Code, (await decision.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(FormTaskState.Open, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormTask>().Where(task => task.Id == seeded.TaskId).Select(task => task.State).SingleAsync(Ct)));
    }

    private async Task<(Guid TaskId, Guid RecordId, Guid DefinitionId, Guid RuleSetId, Guid DeciderId)> SeedApprovalAsync(HttpClient author, HttpClient decider, int hour, TimeSpan? approvalDelay = null)
    {
        var sheet = await SeedSheetAsync(author, 0);
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var deciderId = Guid.Parse(decider.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var roleId = await db.UserRoles.Where(link => link.UserId == deciderId).Select(link => link.RoleId).SingleAsync(Ct);
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(value => value.Id == sheet.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(value => value.Id == definition.ActiveVersionId, Ct);
            var approval = new FormRuleAction(Guid.NewGuid(), "RequestApproval", roleId, null, false);
            var received = new FormRule(Guid.NewGuid(), "Received", [], [approval]);
            var notify = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { { "es", "Respuesta {número}: {motivo}" } }, false);
            var rules = new[] { received, new FormRule(Guid.NewGuid(), "Approved", [], [notify]), new FormRule(Guid.NewGuid(), "Rejected", [], [notify with { Id = Guid.NewGuid() }]) };
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, "es", FormDraftPolicy.Serialize(rules), true, version.SchemaJson); db.Set<BusinessFormRuleSet>().Add(ruleSet); await db.SaveChangesAsync(Ct); definition.SetActiveRuleSetId(ruleSet.Id);
            var at = new DateTime(2026, 10, 16, hour, 0, 0, DateTimeKind.Utc);
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), authorId, Guid.NewGuid(), at, "[]", ruleSet.Id); record.MarkPendingApproval(); db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(record.SourceReceiptId, definition.Id, version.Id, "test-approval", record.Id, record.Number, at));
            var config = new FormActionSnapshot(approval.RoleId, null, null, []);
            var run = BusinessFormActionRun.Create(definition.Id, record.Id, ruleSet.Id, received.Id, approval.Id, "Received", "RequestApproval", 0, null, FormDraftPolicy.Serialize(config), true, at); db.Set<BusinessFormActionRun>().Add(run);
            var task = BusinessFormTask.Open(definition.Id, record.Id, ruleSet.Id, run.Id, roleId, authorId, at.Add(approvalDelay ?? TimeSpan.Zero)); db.Set<BusinessFormTask>().Add(task); await db.SaveChangesAsync(Ct);
            return (task.Id, record.Id, definition.Id, ruleSet.Id, deciderId);
        });
    }
}
