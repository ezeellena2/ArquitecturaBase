using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed partial class FormRuleEndpointsTests
{
    [Fact]
    public async Task Approval_routes_require_decide_and_an_authenticated_session_without_record_read()
    {
        using var anonymous = factory.CreateClient();
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        foreach (var route in new[] { "/api/forms/approvals", "/api/forms/approvals/count", "/api/forms/approvals/" + Guid.NewGuid() })
        {
            using var missing = await anonymous.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
            using var denied = await reader.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            using var allowed = await decider.GetAsync(route, Ct);
            Assert.Equal(route.EndsWith("approvals", StringComparison.Ordinal) || route.EndsWith("count", StringComparison.Ordinal) ? HttpStatusCode.OK : HttpStatusCode.NotFound, allowed.StatusCode);
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
        using var overview = await decider.GetAsync("/api/forms/overview", Ct);
        Assert.Equal(HttpStatusCode.OK, overview.StatusCode);
        Assert.Equal(first.DefinitionId, Assert.Single((await overview.ReadJsonAsync()).GetProperty("forms").GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
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

    [Theory]
    [InlineData("corrected")]
    [InlineData("document")]
    public async Task Whoever_set_the_values_of_a_response_neither_sees_it_as_decidable_nor_decides_it(string how)
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var seeded = await SeedApprovalAsync(author, decider, 12);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var record = await db.Set<BusinessFormRecord>().SingleAsync(value => value.Id == seeded.RecordId, Ct);
            if (how == "corrected")
            {
                var original = BusinessFormRecord.Create(record.DefinitionId, record.VersionId, record.Number + 1000, record.ActorUserId, Guid.NewGuid(), record.SubmittedAtUtc, "[]", record.RuleSetId);
                db.Set<BusinessFormRecord>().Add(original);
                await db.SaveChangesAsync(Ct);
                db.Entry(record).Property("ReplacesRecordId").CurrentValue = original.Id;
                db.Entry(record).Property("OriginalReceiptId").CurrentValue = record.SourceReceiptId;
                db.Entry(record).Property("CorrectionReason").CurrentValue = "Corrección";
                db.Entry(record).Property("CorrectedByUserId").CurrentValue = seeded.DeciderId;
            }
            else
            {
                var document = FormReceivedDocument.Receive(Guid.NewGuid(), "test-channel", Guid.NewGuid().ToString("N"), record.ActorUserId, record.SubmittedAtUtc,
                    "inbox/" + Guid.NewGuid().ToString("N"), "document.pdf", "application/pdf", 10, new string('a', 64), 1, null).Value;
                db.Set<FormReceivedDocument>().Add(document);
                db.Entry(document).Property("RecordId").CurrentValue = seeded.RecordId;
                db.Entry(document).Property("State").CurrentValue = FormReceivedDocumentState.Confirmed;
                db.Entry(document).Property("ReviewedByUserId").CurrentValue = seeded.DeciderId;
                db.Entry(document).Property("ReviewedAtUtc").CurrentValue = record.SubmittedAtUtc;
            }
            await db.SaveChangesAsync(Ct);
        });
        using var inbox = await decider.GetAsync("/api/forms/approvals", Ct);
        Assert.Equal(0, (await inbox.ReadJsonAsync()).GetProperty("totalCount").GetInt32());
        using var count = await decider.GetAsync("/api/forms/approvals/count?definitionId=" + seeded.DefinitionId, Ct);
        Assert.Equal(0, (await count.ReadJsonAsync()).GetProperty("count").GetInt32());
        using var all = await decider.GetAsync("/api/forms/approvals?onlyMine=false", Ct);
        var item = Assert.Single((await all.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.False(item.GetProperty("canDecide").GetBoolean());
        Assert.Equal("OwnResponse", item.GetProperty("cannotDecideReason").GetString());
        using var decision = await decider.PostAsJsonAsync("/api/forms/approvals/" + seeded.TaskId + "/decisions", new { expectedRevision = 1, decision = "Approved" }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, decision.StatusCode);
        Assert.Equal(FormApprovalErrors.OwnResponse.Code, (await decision.ReadJsonAsync()).GetProperty("code").GetString());
    }

    // RC4-A: «Puede decidir quien cargó» lets the author decide and counts the task for them, in the inbox and in the overview, and without
    // «Exigir motivo al rechazar» a rejection goes without a reason; the task says so, for the dialog.
    [Fact]
    public async Task An_approval_that_lets_its_author_decide_and_leaves_the_reason_optional_is_rejected_by_its_author_without_a_reason()
    {
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var own = await SeedApprovalAsync(decider, decider, 9, authorMayDecide: true, reasonOptional: true);
        using var mine = await decider.GetAsync("/api/forms/approvals", Ct);
        var item = Assert.Single((await mine.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal(own.TaskId, item.GetProperty("id").GetGuid());
        Assert.True(item.GetProperty("canDecide").GetBoolean());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, item.GetProperty("cannotDecideReason").ValueKind);
        Assert.True(item.GetProperty("reasonOptional").GetBoolean());
        using var count = await decider.GetAsync("/api/forms/approvals/count?definitionId=" + own.DefinitionId, Ct);
        Assert.Equal(1, (await count.ReadJsonAsync()).GetProperty("count").GetInt32());
        using var overview = await decider.GetAsync("/api/forms/overview", Ct);
        var form = Assert.Single((await overview.ReadJsonAsync()).GetProperty("forms").GetProperty("items").EnumerateArray(), row => row.GetProperty("id").GetGuid() == own.DefinitionId);
        Assert.Equal(1, form.GetProperty("pendingApprovalCount").GetInt32());
        using var decision = await decider.PostAsJsonAsync("/api/forms/approvals/" + own.TaskId + "/decisions", new { expectedRevision = 1, decision = "Rejected" }, Ct);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await decision.ReadJsonAsync()).GetProperty("reason").ValueKind);
        var stored = await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormTask>().AsNoTracking().SingleAsync(task => task.Id == own.TaskId, Ct));
        Assert.Equal((FormTaskState.Rejected, (string?)null, true, true), (stored.State, stored.Reason, stored.AuthorMayDecide, stored.ReasonOptional));
    }
    [Fact]
    public async Task An_approval_that_asks_for_a_reason_refuses_a_rejection_without_one_on_its_field()
    {
        using var author = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var decider = await OperatorAsync([BusinessFormsPermissions.ApprovalsDecide]);
        var seeded = await SeedApprovalAsync(author, decider, 8);
        using var task = await decider.GetAsync("/api/forms/approvals/" + seeded.TaskId, Ct);
        Assert.False((await task.ReadJsonAsync()).TryGetProperty("reasonOptional", out _));
        using var missing = await decider.PostAsJsonAsync("/api/forms/approvals/" + seeded.TaskId + "/decisions", new { expectedRevision = 1, decision = "Rejected", reason = "   " }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var problem = await missing.ReadJsonAsync();
        Assert.Equal(FormApprovalErrors.ReasonRequired.Code, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("reason", out _));
        Assert.Equal(FormTaskState.Open, await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormTask>().Where(value => value.Id == seeded.TaskId).Select(value => value.State).SingleAsync(Ct)));
    }
}
