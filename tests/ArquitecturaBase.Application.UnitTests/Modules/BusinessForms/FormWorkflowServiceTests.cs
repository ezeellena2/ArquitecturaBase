using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormWorkflowServiceTests
{
    [Fact]
    public async Task Reading_workflow_requires_record_read_before_accessing_private_state()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([]);var reads=0;fixture.Runs.BeforeWorkflowReturn=()=>reads++;
        var result=await Service(fixture).GetAsync(captured.Record.Id,CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden,result.Error);Assert.Equal(0,reads);
    }
    [Fact]
    public async Task Revocation_during_workflow_read_discards_actions_tasks_and_documents()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([]);Allow(fixture);
        fixture.Runs.BeforeWorkflowReturn=()=>fixture.Rules.Permissions.Permissions.Clear();
        var result=await Service(fixture).GetAsync(captured.Record.Id,CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden,result.Error);
    }
    [Fact]
    public async Task A_reader_without_rule_management_sees_state_but_cannot_consult_or_retry()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[FormActionFixture.Action()])]);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var run=Assert.Single(fixture.Runs.Runs);var now=fixture.Clock.GetUtcNow().UtcDateTime;var lease=run.Reserve(now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(lease.Fence,now,FormActionRunState.Uncertain,"unknown",false,FormDraftPolicy.Serialize(new FormActionOutcomeResponse(FormActionRunState.Uncertain,Recipients:[new(captured.Record.ActorUserId,"Author","phone",new(StructuredRecordActionState.Uncertain))])));
        fixture.Rules.Permissions.Permissions[fixture.Rules.User.UserId!.Value]=[BusinessFormsPermissions.RecordsRead];var service=Service(fixture);
        var result=await service.GetAsync(captured.Record.Id,CancellationToken.None);
        Assert.True(result.IsSuccess);Assert.False(result.Value.Actions[0].CanConsult);Assert.False(result.Value.Actions[0].CanRetry);Assert.DoesNotContain(result.Value.Actions[0].Recipients,recipient=>recipient.CanReview);
        Assert.Equal(FormDefinitionErrors.Forbidden,(await service.RetryAsync(captured.Record.Id,run.Id,CancellationToken.None)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden,(await service.ConsultAsync(captured.Record.Id,run.Id,CancellationToken.None)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden,(await service.ReviewAsync(captured.Record.Id,run.Id,captured.Record.ActorUserId,run.Fence,true,CancellationToken.None)).Error);
        Assert.Equal(0,fixture.Rules.UnitOfWork.Transactions);
    }
    [Fact]
    public async Task Permission_revocation_inside_the_transaction_prevents_a_retry()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[FormActionFixture.Action()])]);Allow(fixture);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var run=Assert.Single(fixture.Runs.Runs);fixture.Rules.BeforeLock=()=>fixture.Rules.Permissions.Permissions.Clear();
        var result=await Service(fixture).RetryAsync(captured.Record.Id,run.Id,CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden,result.Error);Assert.False(run.RetryRequested);Assert.Equal(1,fixture.Rules.UnitOfWork.Rollbacks);
    }
    [Fact]
    public async Task Another_record_cannot_reach_the_run_and_confirmed_actions_cannot_be_reopened()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[FormActionFixture.Action()])]);Allow(fixture);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var run=Assert.Single(fixture.Runs.Runs);var now=fixture.Clock.GetUtcNow().UtcDateTime;var lease=run.Reserve(now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(lease.Fence,now,FormActionRunState.Completed,null,false);var service=Service(fixture);
        Assert.Equal(FormActionErrors.NotFound,(await service.RetryAsync(Guid.NewGuid(),run.Id,CancellationToken.None)).Error);
        Assert.True((await service.RetryAsync(captured.Record.Id,run.Id,CancellationToken.None)).IsSuccess);
        Assert.Equal(FormActionRunState.Completed,run.State);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reviewing_an_uncertain_notification_queues_the_original_actor_and_keeps_the_decision_visible(bool wasSent)
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[FormActionFixture.Action()])]);Allow(fixture);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var run=Assert.Single(fixture.Runs.Runs);var now=fixture.Clock.GetUtcNow().UtcDateTime;var lease=run.Reserve(now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(lease.Fence,now,FormActionRunState.Uncertain,"unknown",false,FormDraftPolicy.Serialize(new FormActionOutcomeResponse(FormActionRunState.Uncertain,Recipients:[new(captured.Record.ActorUserId,"Author","phone",new(StructuredRecordActionState.Uncertain))])));var service=Service(fixture);
        var before=(await service.GetAsync(captured.Record.Id,CancellationToken.None)).Value.Actions[0];
        Assert.Contains(before.Recipients,recipient=>recipient.CanReview);Assert.False(before.CanRetry);
        Assert.Equal(FormActionErrors.StateConflict,(await service.RetryAsync(captured.Record.Id,run.Id,CancellationToken.None)).Error);
        var response=await service.ReviewAsync(captured.Record.Id,run.Id,captured.Record.ActorUserId,run.Fence,wasSent,CancellationToken.None);
        Assert.True(response.IsSuccess);Assert.True(run.DeliveryReviewRequested);Assert.False(run.RetryRequested);
        var reviewed=Assert.Single(Assert.Single(response.Value.Actions).Recipients).Review;Assert.NotNull(reviewed);Assert.Equal(wasSent,reviewed.WasSent);
        Assert.Equal(fixture.Rules.User.UserId,reviewed.ReviewedBy.UserId);Assert.Equal(now,reviewed.ReviewedAtUtc);
    }
    [Fact]
    public async Task Revocation_inside_the_review_transaction_cannot_record_a_human_decision()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[FormActionFixture.Action()])]);Allow(fixture);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var run=Assert.Single(fixture.Runs.Runs);var now=fixture.Clock.GetUtcNow().UtcDateTime;var lease=run.Reserve(now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(lease.Fence,now,FormActionRunState.Uncertain,"unknown",false,FormDraftPolicy.Serialize(new FormActionOutcomeResponse(FormActionRunState.Uncertain,Recipients:[new(captured.Record.ActorUserId,"Author","phone",new(StructuredRecordActionState.Uncertain))])));
        fixture.Rules.BeforeLock=()=>fixture.Rules.Permissions.Permissions.Clear();
        var result=await Service(fixture).ReviewAsync(captured.Record.Id,run.Id,captured.Record.ActorUserId,run.Fence,false,CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden,result.Error);Assert.False(run.DeliveryReviewRequested);Assert.Null(run.DeliveryReviewWasSent);
        Assert.Equal(1,fixture.Rules.UnitOfWork.Rollbacks);
    }
    [Fact]
    public async Task Review_rejects_people_outside_the_snapshot_and_people_without_uncertain_evidence()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[FormActionFixture.Action()])]);Allow(fixture);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var run=Assert.Single(fixture.Runs.Runs);var now=fixture.Clock.GetUtcNow().UtcDateTime;var lease=run.Reserve(now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(lease.Fence,now,FormActionRunState.Uncertain,"unknown",false);
        var service=Service(fixture);
        Assert.Equal(FormActionErrors.StateConflict,(await service.ReviewAsync(captured.Record.Id,run.Id,Guid.NewGuid(),run.Fence,true,CancellationToken.None)).Error);
        Assert.Equal(FormActionErrors.StateConflict,(await service.ReviewAsync(captured.Record.Id,run.Id,captured.Record.ActorUserId,run.Fence,true,CancellationToken.None)).Error);
        Assert.False(run.DeliveryReviewRequested);Assert.Null(run.DeliveryReviewRecipientUserId);
    }
    [Fact]
    public async Task A_person_with_a_confirmed_failure_in_a_completed_role_notice_is_queued_alone_for_a_retry()
    {
        var notice=await RoleNoticeAsync();var service=Service(notice.Fixture);var expected=notice.Run.Fence;
        var response=await service.RetryRecipientAsync(notice.RecordId,notice.Run.Id,notice.Failed,expected,CancellationToken.None);
        Assert.True(response.IsSuccess);Assert.Equal("Waiting",Assert.Single(response.Value.Actions).State);
        Assert.Equal(FormActionRunState.Waiting,notice.Run.State);Assert.True(notice.Run.RetryRequested);Assert.Equal(notice.Failed,notice.Run.RetryRecipientUserId);
        Assert.True((await service.RetryRecipientAsync(notice.RecordId,notice.Run.Id,notice.Failed,expected,CancellationToken.None)).IsSuccess);
        Assert.Equal(notice.Failed,notice.Run.RetryRecipientUserId);Assert.Equal(expected,notice.Run.Fence);
    }
    [Fact]
    public async Task Retrying_a_person_needs_a_confirmed_failure_the_current_revision_and_rule_management()
    {
        var notice=await RoleNoticeAsync();var service=Service(notice.Fixture);var expected=notice.Run.Fence;
        foreach(var person in new[]{notice.Accepted,notice.Changed,Guid.NewGuid()})
            Assert.Equal(FormActionErrors.StateConflict,(await service.RetryRecipientAsync(notice.RecordId,notice.Run.Id,person,expected,CancellationToken.None)).Error);
        Assert.Equal(FormActionErrors.StateConflict,(await service.RetryRecipientAsync(notice.RecordId,notice.Run.Id,notice.Failed,expected-1,CancellationToken.None)).Error);
        Assert.Equal(FormActionErrors.NotFound,(await service.RetryRecipientAsync(Guid.NewGuid(),notice.Run.Id,notice.Failed,expected,CancellationToken.None)).Error);
        notice.Fixture.Rules.BeforeLock=()=>notice.Fixture.Rules.Permissions.Permissions.Clear();
        Assert.Equal(FormDefinitionErrors.Forbidden,(await service.RetryRecipientAsync(notice.RecordId,notice.Run.Id,notice.Failed,expected,CancellationToken.None)).Error);
        Assert.Equal(FormActionRunState.Completed,notice.Run.State);Assert.False(notice.Run.RetryRequested);Assert.Null(notice.Run.RetryRecipientUserId);
    }
    private static async Task<(FormActionFixture Fixture,Guid RecordId,BusinessFormActionRun Run,Guid Accepted,Guid Failed,Guid Changed)> RoleNoticeAsync()
    {
        var fixture=new FormActionFixture();var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[FormActionFixture.Action("NotifyRole") with{RoleId=fixture.Rules.RoleId}])]);Allow(fixture);
        var accepted=captured.Record.ActorUserId;var failed=Guid.NewGuid();var changed=Guid.NewGuid();
        fixture.Recipients.People[failed]=new(failed,"es","Failed employee");fixture.Recipients.People[changed]=new(changed,"es","Changed employee");
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);
        var run=Assert.Single(fixture.Runs.Runs);var now=fixture.Clock.GetUtcNow().UtcDateTime;var lease=run.Reserve(now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(lease.Fence,now,FormActionRunState.Completed,null,false,FormDraftPolicy.Serialize(new FormActionOutcomeResponse(FormActionRunState.Completed,Recipients:[
            new(accepted,"Author","phone",new(StructuredRecordActionState.Accepted)),
            new(failed,"Failed employee","phone",new(StructuredRecordActionState.Failed,"WhatsApp.FormAction.RecipientUnavailable",true)),
            new(changed,"Changed employee","phone",new(StructuredRecordActionState.Failed,"WhatsApp.FormAction.RecipientChanged",false))])));
        return(fixture,captured.Record.Id,run,accepted,failed,changed);
    }
    private static void Allow(FormActionFixture fixture)=>fixture.Rules.Permissions.Permissions[fixture.Rules.User.UserId!.Value]=[BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RulesManage];
    private static FormWorkflowService Service(FormActionFixture fixture)=>new(new(fixture.Runs,new(fixture.Rules.User,fixture.Rules.Permissions,new FakeFormResponseReader()),fixture.Rules.Permissions,[]),fixture.Runs,fixture.Rules,new(fixture.Rules.User,fixture.Rules.Permissions,new FakeFormResponseReader()),
        fixture.Rules.Permissions,fixture.Rules.UnitOfWork,fixture.Clock,NullLogger<FormWorkflowService>.Instance);
}
