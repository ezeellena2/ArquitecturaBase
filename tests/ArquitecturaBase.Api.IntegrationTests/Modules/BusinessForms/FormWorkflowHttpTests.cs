using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Workflow_routes_require_record_read_and_controls_also_require_rule_management()
    {
        using var anonymous = factory.CreateClient();
        using var designer = await OperatorAsync([BusinessFormsPermissions.Read]);
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RulesManage]);
        using var both = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var recordId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var route = $"/api/forms/records/{recordId}/workflow";
        using var missingSession = await anonymous.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, missingSession.StatusCode);
        using var denied = await designer.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var missing = await reader.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        foreach (var action in new[] { "consult", "retry" })
        {
            var control = $"/api/forms/records/{recordId}/actions/{runId}/{action}";
            using var unauthenticated = await anonymous.PostAsync(control, null, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            using var readOnly = await reader.PostAsync(control, null, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
            using var manageOnly = await manager.PostAsync(control, null, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, manageOnly.StatusCode);
            using var allowed = await both.PostAsync(control, null, Ct);
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }
    }

    [Fact]
    public async Task Workflow_retries_only_the_selected_response_and_never_repeats_a_completed_run()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        var seeded = await SeedWorkflowRunAsync(reader, FormActionRunState.Failed);
        var route = $"/api/forms/records/{seeded.RecordId}/workflow";
        using var readOnly = await reader.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, readOnly.StatusCode);
        Assert.False((await readOnly.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("canRetry").GetBoolean());
        foreach (var culture in new[] { "es", "en" })
        {
            manager.DefaultRequestHeaders.AcceptLanguage.Clear(); manager.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var view = await manager.GetAsync(route, Ct);
            Assert.Equal(HttpStatusCode.OK, view.StatusCode);
            Assert.True((await view.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("canRetry").GetBoolean());
            await FormWorkflowContractCapture.WriteAsync(view, "failed-" + culture, Ct);
        }
        using var mismatch = await manager.PostAsync($"/api/forms/records/{Guid.NewGuid()}/actions/{seeded.RunId}/retry", null, Ct);
        Assert.Equal(HttpStatusCode.NotFound, mismatch.StatusCode);
        using var retry = await manager.PostAsync($"/api/forms/records/{seeded.RecordId}/actions/{seeded.RunId}/retry", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.Equal(route, retry.Headers.Location!.ToString());
        Assert.Equal("Waiting", (await retry.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("state").GetString());
        await FormWorkflowContractCapture.WriteAsync(retry, "retry-en", Ct);
        var completed = await SeedWorkflowRunAsync(reader, FormActionRunState.Completed);
        using var repeat = await manager.PostAsync($"/api/forms/records/{completed.RecordId}/actions/{completed.RunId}/retry", null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, repeat.StatusCode);
        Assert.Equal("Completed", (await repeat.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("state").GetString());
        await FormWorkflowContractCapture.WriteAsync(repeat, "completed-en", Ct);
        await factory.ExecuteDbContextAsync(async db =>
        {
            var run = await db.Set<BusinessFormActionRun>().SingleAsync(run => run.Id == completed.RunId, Ct);
            Assert.Equal(1, run.Fence);
            Assert.Equal(1, await db.Set<BusinessFormActionRun>().CountAsync(run => run.RecordId == completed.RecordId, Ct));
            Assert.False(run.RetryRequested);
        });
    }

    [Fact]
    public async Task Consulting_an_uncertain_run_preserves_read_access_when_control_permission_is_revoked()
    {
        using var manager = await OperatorAsync([BusinessFormsPermissions.RecordsRead, BusinessFormsPermissions.RulesManage]);
        using var administrator = await AdminAsync();
        var seeded = await SeedWorkflowRunAsync(manager, FormActionRunState.Uncertain);
        var route = $"/api/forms/records/{seeded.RecordId}/workflow";
        var control = $"/api/forms/records/{seeded.RecordId}/actions/{seeded.RunId}/consult";
        using var uncertain = await manager.GetAsync(route, Ct);
        Assert.True((await uncertain.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("canConsult").GetBoolean());
        await FormWorkflowContractCapture.WriteAsync(uncertain, "uncertain-es", Ct);
        using var consultation = await manager.PostAsync(control, null, Ct);
        Assert.Equal(HttpStatusCode.Accepted, consultation.StatusCode);
        await FormWorkflowContractCapture.WriteAsync(consultation, "consult-es", Ct);
        var actor = Guid.Parse(manager.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var role = await factory.ExecuteDbContextAsync(db => db.UserRoles.Where(link => link.UserId == actor)
            .Join(db.Roles, link => link.RoleId, item => item.Id, (link, item) => new { item.Id, item.Name }).SingleAsync(Ct));
        using var revokeControl = await administrator.PutAsJsonAsync("/api/roles/" + role.Id,
            new { name = role.Name, description = (string?)null, permissions = new[] { BusinessFormsPermissions.RecordsRead } }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeControl.StatusCode);
        using var deniedControl = await manager.PostAsync(control, null, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedControl.StatusCode);
        await FormWorkflowContractCapture.WriteAsync(deniedControl, "control-forbidden-es", Ct);
        using var stillReadable = await manager.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.OK, stillReadable.StatusCode);
        Assert.False((await stillReadable.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("canConsult").GetBoolean());
        using var revokeRead = await administrator.PutAsJsonAsync("/api/roles/" + role.Id,
            new { name = role.Name, description = (string?)null, permissions = Array.Empty<string>() }, Ct);
        Assert.Equal(HttpStatusCode.NoContent, revokeRead.StatusCode);
        using var deniedRead = await manager.GetAsync(route, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        await FormWorkflowContractCapture.WriteAsync(deniedRead, "read-forbidden-es", Ct);
    }

    [Fact]
    public async Task Workflow_describes_waiting_running_and_not_applicable_without_exposing_action_templates()
    {
        using var reader = await OperatorAsync([BusinessFormsPermissions.RecordsRead]);
        foreach (var state in new[] { FormActionRunState.Waiting, FormActionRunState.Running, FormActionRunState.NotApplicable })
        {
            var seeded = await SeedWorkflowRunAsync(reader, state);
            using var response = await reader.GetAsync($"/api/forms/records/{seeded.RecordId}/workflow", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var action = (await response.ReadJsonAsync()).GetProperty("actions")[0];
            Assert.Equal(state.ToString(), action.GetProperty("state").GetString());
            Assert.False(action.GetProperty("canRetry").GetBoolean());
            Assert.False(action.GetProperty("canConsult").GetBoolean());
            Assert.DoesNotContain("Recibimos", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
            var name = state == FormActionRunState.NotApplicable ? "not-applicable" : state.ToString().ToLowerInvariant();
            await FormWorkflowContractCapture.WriteAsync(response, name + "-es", Ct);
        }
    }

    private async Task<(Guid RecordId, Guid RunId)> SeedWorkflowRunAsync(HttpClient actorClient, FormActionRunState state)
    {
        var sheet = await SeedSheetAsync(actorClient, 0);
        var actor = Guid.Parse(actorClient.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        return await factory.ExecuteDbContextAsync(async db =>
        {
            var definition = await db.Set<BusinessFormDefinition>().SingleAsync(item => item.Id == sheet.Id, Ct);
            var version = await db.Set<BusinessFormVersion>().SingleAsync(item => item.Id == definition.ActiveVersionId, Ct);
            var action = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string, string> { ["es"] = "Recibimos {número}" }, false);
            var rule = new FormRule(Guid.NewGuid(), "Received", [], [action]);
            var ruleSet = BusinessFormRuleSet.Create(definition.Id, 1, "es", FormDraftPolicy.Serialize(new[] { rule }), false, version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(ruleSet);
            await db.SaveChangesAsync(Ct);
            definition.SetActiveRuleSetId(ruleSet.Id);
            var now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
            var record = BusinessFormRecord.Create(definition.Id, version.Id, definition.AllocateRecordNumber(), actor, Guid.NewGuid(), now, "[]", ruleSet.Id);
            db.Set<BusinessFormRecord>().Add(record);
            db.Set<BusinessFormCaptureReceipt>().Add(BusinessFormCaptureReceipt.Complete(record.SourceReceiptId, definition.Id, version.Id, "test-workflow", record.Id, record.Number, now));
            var row = new FormRecordStoredRow(record.SourceReceiptId, record.Id, record.Number, definition.Id, definition.Prefix, version.Id, version.Number,
                version.NameJson!, version.SchemaJson, record.ValuesJson);
            var snapshot = new FormActionRecordSnapshot(new(row, now, new(actor, "Author")), "es");
            var config = new FormActionSnapshot(snapshot, action, FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson), null, null, [], null);
            var run = BusinessFormActionRun.Create(definition.Id, record.Id, ruleSet.Id, rule.Id, action.Id, "Received", "NotifyAuthor", 0, null,
                FormDraftPolicy.Serialize(config), state != FormActionRunState.NotApplicable, now);
            if (state is not (FormActionRunState.Waiting or FormActionRunState.NotApplicable))
            {
                var reservation = run.Reserve(now, TimeSpan.FromMinutes(2));
                Assert.True(reservation.IsSuccess);
                Assert.True(run.RecordOutcome(reservation.Value.Fence, now, state, state == FormActionRunState.Completed ? null : FormActionErrors.EffectUnconfirmed.Code,
                    state == FormActionRunState.Failed).IsSuccess);
            }
            db.Set<BusinessFormActionRun>().Add(run);
            await db.SaveChangesAsync(Ct);
            return (record.Id, run.Id);
        });
    }
}
