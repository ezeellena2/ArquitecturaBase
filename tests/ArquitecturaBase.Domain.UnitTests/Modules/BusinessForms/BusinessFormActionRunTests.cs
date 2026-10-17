using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;
public sealed class BusinessFormActionRunTests
{
    private static readonly DateTime Now = new(2026,10,16,12,0,0,DateTimeKind.Utc);
    [Fact]
    public void A_lease_excludes_another_worker_and_a_stale_fence_cannot_complete()
    {
        var run = Create();
        var first = run.Reserve(Now,TimeSpan.FromMinutes(2));
        Assert.True(first.IsSuccess);
        Assert.False(first.Value.ReconciliationOnly);
        Assert.True(run.Reserve(Now,TimeSpan.FromMinutes(2)).IsFailure);
        var replacement = run.Reserve(Now.AddMinutes(3),TimeSpan.FromMinutes(2));
        Assert.True(replacement.Value.ReconciliationOnly);
        Assert.True(run.RecordOutcome(first.Value.Fence,Now.AddMinutes(3),FormActionRunState.Completed,null,false).IsFailure);
        Assert.True(run.RecordOutcome(replacement.Value.Fence,Now.AddMinutes(3),FormActionRunState.Completed,null,false).IsSuccess);
    }
    [Fact]
    public void Confirmed_actions_are_never_reopened_by_retry_or_consult()
    {
        var run = Create();
        var lease = run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        Assert.True(run.RecordOutcome(lease.Fence,Now,FormActionRunState.Completed,null,false).IsSuccess);
        Assert.True(run.RequestRetry(Now).IsSuccess);
        Assert.True(run.RequestConsult(Now).IsSuccess);
        Assert.Equal(FormActionRunState.Completed,run.State);
        Assert.True(run.Reserve(Now.AddHours(1),TimeSpan.FromMinutes(2)).IsFailure);
    }
    [Fact]
    public void Uncertain_actions_must_be_reconciled_and_known_failed_actions_can_resume()
    {
        var run = Create();
        var lease = run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        Assert.True(run.RecordOutcome(lease.Fence,Now,FormActionRunState.Uncertain,"unknown",false).IsSuccess);
        Assert.True(run.RequestRetry(Now).IsSuccess);
        var retry = run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        Assert.True(retry.ReconciliationOnly);
        Assert.True(run.RetryRequested);
        Assert.True(run.RecordOutcome(retry.Fence,Now,FormActionRunState.Failed,"absent",true).IsSuccess);
        Assert.True(run.RequestRetry(Now).IsSuccess);
        Assert.False(run.Reserve(Now,TimeSpan.FromMinutes(2)).Value.ReconciliationOnly);
    }
    [Fact]
    public void Uncertain_actions_wait_for_an_explicit_consult_before_they_can_be_reserved()
    {
        var run = Create();
        var lease = run.Reserve(Now, TimeSpan.FromMinutes(2)).Value;
        Assert.True(run.RecordOutcome(lease.Fence, Now, FormActionRunState.Uncertain, "unknown", false).IsSuccess);
        Assert.True(run.Reserve(Now.AddHours(1), TimeSpan.FromMinutes(2)).IsFailure);
        Assert.Null(run.NextCheckAtUtc);
        Assert.True(run.RequestConsult(Now.AddHours(1)).IsSuccess);
        var consult = run.Reserve(Now.AddHours(1), TimeSpan.FromMinutes(2));
        Assert.True(consult.IsSuccess);
        Assert.True(consult.Value.ReconciliationOnly);
        Assert.False(run.RetryRequested);
        Assert.True(run.RecordOutcome(consult.Value.Fence, Now.AddHours(1), FormActionRunState.Uncertain, "unknown", false).IsSuccess);
        Assert.True(run.Reserve(Now.AddHours(2), TimeSpan.FromMinutes(2)).IsFailure);
    }
    [Fact]
    public void A_pending_effect_keeps_its_scheduled_reconciliation()
    {
        var run = Create();
        var lease = run.Reserve(Now, TimeSpan.FromMinutes(2)).Value;
        Assert.True(run.RecordOutcome(lease.Fence, Now, FormActionRunState.Running, null, false).IsSuccess);
        Assert.True(run.Reserve(Now.AddSeconds(4), TimeSpan.FromMinutes(2)).IsFailure);
        var next = run.Reserve(Now.AddSeconds(5), TimeSpan.FromMinutes(2));
        Assert.True(next.IsSuccess);
        Assert.True(next.Value.ReconciliationOnly);
    }
    private static BusinessFormActionRun Create() => BusinessFormActionRun.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
        Guid.NewGuid(),Guid.NewGuid(),"Received","NotifyAuthor",0,null,"{}",true,Now);
}
