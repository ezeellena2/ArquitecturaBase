using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        foreach (var action in new[] { "consult", "retry", "review" })
        {
            var control = $"/api/forms/records/{recordId}/actions/{runId}/{action}";
            using var unauthenticated = await anonymous.PostAsJsonAsync(control, new { expectedRevision = 0L, wasSent = false }, Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            using var readOnly = await reader.PostAsJsonAsync(control, new { expectedRevision = 0L, wasSent = false }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
            using var manageOnly = await manager.PostAsJsonAsync(control, new { expectedRevision = 0L, wasSent = false }, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, manageOnly.StatusCode);
            using var allowed = await both.PostAsJsonAsync(control, new { expectedRevision = 0L, wasSent = false }, Ct);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Workflow_review_records_human_evidence_and_never_offers_retry_before_a_confirmed_failure(bool wasSent)
    {
        using var manager=await OperatorAsync([BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RulesManage]);
        var seeded=await SeedWorkflowRunAsync(manager,FormActionRunState.Uncertain);
        var root=$"/api/forms/records/{seeded.RecordId}";var action=$"{root}/actions/{seeded.RunId}";
        using var before=await manager.GetAsync(root+"/workflow",Ct);var item=(await before.ReadJsonAsync()).GetProperty("actions")[0];
        var expectedRevision=item.GetProperty("revision").GetInt64();
        Assert.True(item.GetProperty("canReview").GetBoolean());Assert.False(item.GetProperty("canRetry").GetBoolean());
        using var retry=await manager.PostAsync(action+"/retry",null,Ct);Assert.Equal(HttpStatusCode.Conflict,retry.StatusCode);
        using var missingDecision=await manager.PostAsJsonAsync(action+"/review",new{expectedRevision},Ct);Assert.Equal(HttpStatusCode.BadRequest,missingDecision.StatusCode);
        using var missingRevision=await manager.PostAsJsonAsync(action+"/review",new{wasSent},Ct);Assert.Equal(HttpStatusCode.BadRequest,missingRevision.StatusCode);
        using var extraIdentity=await manager.PostAsJsonAsync(action+"/review",new{expectedRevision,wasSent,reviewedBy=Guid.NewGuid()},Ct);Assert.Equal(HttpStatusCode.BadRequest,extraIdentity.StatusCode);
        using var wrongRecord=await manager.PostAsJsonAsync($"/api/forms/records/{Guid.NewGuid()}/actions/{seeded.RunId}/review",new{expectedRevision,wasSent},Ct);Assert.Equal(HttpStatusCode.NotFound,wrongRecord.StatusCode);
        using var reviewed=await manager.PostAsJsonAsync(action+"/review",new{expectedRevision,wasSent},Ct);Assert.Equal(HttpStatusCode.Accepted,reviewed.StatusCode);
        Assert.Equal(root+"/workflow",reviewed.Headers.Location!.ToString());
        var evidence=(await reviewed.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("review");
        Assert.Equal(wasSent,evidence.GetProperty("wasSent").GetBoolean());
        Assert.Equal(manager.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single(),evidence.GetProperty("reviewedBy").GetProperty("userId").GetString());
        await FormWorkflowContractCapture.WriteAsync(reviewed,wasSent?"review-sent-es":"review-not-sent-es",Ct);
        await factory.ExecuteDbContextAsync(async db=>
        {
            var run=await db.Set<BusinessFormActionRun>().AsNoTracking().SingleAsync(run=>run.Id==seeded.RunId,Ct);
            Assert.True(run.DeliveryReviewRequested);Assert.Equal(wasSent,run.DeliveryReviewWasSent);Assert.False(run.RetryRequested);
            Assert.NotNull(run.DeliveryReviewedAtUtc);Assert.NotNull(run.DeliveryReviewedByUserId);
        });
        using var bypass=await manager.PostAsync(action+"/retry",null,Ct);Assert.Equal(HttpStatusCode.Conflict,bypass.StatusCode);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_worker_resolves_the_queued_review_without_sending_and_keeps_evidence_after_completion(bool wasSent)
    {
        using var manager=await OperatorAsync([BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RulesManage]);
        var actor=Guid.Parse(manager.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var seeded=await SeedWorkflowRunAsync(manager,FormActionRunState.Uncertain);var channel=new CapturingFormActionChannel();
        channel.SetState(seeded.RunId,actor,new(StructuredRecordActionState.Uncertain));
        await using var workerHost=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {
            services.RemoveAll<IStructuredRecordActionChannel>();services.AddSingleton<IStructuredRecordActionChannel>(channel);
        }));
        var root=$"/api/forms/records/{seeded.RecordId}";
        using var pending=await manager.GetAsync(root+"/workflow",Ct);var expectedRevision=(await pending.ReadJsonAsync()).GetProperty("actions")[0].GetProperty("revision").GetInt64();
        using var review=await manager.PostAsJsonAsync($"{root}/actions/{seeded.RunId}/review",new{expectedRevision,wasSent},Ct);Assert.Equal(HttpStatusCode.Accepted,review.StatusCode);
        await workerHost.Services.GetRequiredService<FormActionBackgroundService>().ProcessNextAsync(seeded.RunId,Ct);
        Assert.Empty(channel.Messages);Assert.Equal(actor,Assert.Single(channel.Reviews).ReviewedByUserId);
        using var response=await manager.GetAsync(root+"/workflow",Ct);var action=(await response.ReadJsonAsync()).GetProperty("actions")[0];
        Assert.Equal(wasSent?"Completed":"Failed",action.GetProperty("state").GetString());Assert.Equal(!wasSent,action.GetProperty("canRetry").GetBoolean());
        Assert.False(action.GetProperty("canReview").GetBoolean());Assert.Equal(wasSent,action.GetProperty("review").GetProperty("wasSent").GetBoolean());
        using var replay=await manager.PostAsJsonAsync($"{root}/actions/{seeded.RunId}/review",new{expectedRevision,wasSent},Ct);
        Assert.Equal(HttpStatusCode.Accepted,replay.StatusCode);
        var repeated=(await replay.ReadJsonAsync()).GetProperty("actions")[0];Assert.Equal(action.GetProperty("review").GetRawText(),repeated.GetProperty("review").GetRawText());
        Assert.Equal(action.GetProperty("revision").GetInt64(),repeated.GetProperty("revision").GetInt64());
        using var stale=await manager.PostAsJsonAsync($"{root}/actions/{seeded.RunId}/review",new{expectedRevision,wasSent=!wasSent},Ct);Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        await FormWorkflowContractCapture.WriteAsync(response,wasSent?"review-completed-es":"review-failed-es",Ct);
        await factory.ExecuteDbContextAsync(async db=>
        {
            var run=await db.Set<BusinessFormActionRun>().AsNoTracking().SingleAsync(run=>run.Id==seeded.RunId,Ct);
            Assert.False(run.DeliveryReviewRequested);Assert.Equal(wasSent,run.DeliveryReviewWasSent);Assert.Equal(actor,run.DeliveryReviewedByUserId);
        });
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
            var config = new FormActionSnapshot(null, null, null, [new(actor, "es", "Recibimos la respuesta")], null);
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
