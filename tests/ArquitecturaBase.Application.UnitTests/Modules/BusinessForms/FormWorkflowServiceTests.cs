using ArquitecturaBase.Application.Modules.BusinessForms.Services;
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
        run.RecordOutcome(lease.Fence,now,FormActionRunState.Uncertain,"unknown",false);
        fixture.Rules.Permissions.Permissions[fixture.Rules.User.UserId!.Value]=[BusinessFormsPermissions.RecordsRead];var service=Service(fixture);
        var result=await service.GetAsync(captured.Record.Id,CancellationToken.None);
        Assert.True(result.IsSuccess);Assert.False(result.Value.Actions[0].CanConsult);Assert.False(result.Value.Actions[0].CanRetry);
        Assert.Equal(FormDefinitionErrors.Forbidden,(await service.RetryAsync(captured.Record.Id,run.Id,CancellationToken.None)).Error);
        Assert.Equal(FormDefinitionErrors.Forbidden,(await service.ConsultAsync(captured.Record.Id,run.Id,CancellationToken.None)).Error);
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
    private static void Allow(FormActionFixture fixture)=>fixture.Rules.Permissions.Permissions[fixture.Rules.User.UserId!.Value]=[BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RulesManage];
    private static FormWorkflowService Service(FormActionFixture fixture)=>new(fixture.Runs,fixture.Runs,fixture.Rules,new(fixture.Rules.User,fixture.Rules.Permissions),
        fixture.Rules.Permissions,fixture.Rules.UnitOfWork,fixture.Clock,NullLogger<FormWorkflowService>.Instance);
}
