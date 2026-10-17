using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Domain.UnitTests.Modules.BusinessForms;
public sealed class BusinessFormActionRunTests
{
    private static readonly Guid Recipient=Guid.NewGuid();
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
        Assert.True(run.RequestRetry(Now).IsFailure);
        Assert.True(run.RequestConsult(Now).IsSuccess);
        var retry = run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        Assert.True(retry.ReconciliationOnly);
        Assert.False(run.RetryRequested);
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
    [Fact]
    public void Consulting_uncertainty_or_waiting_for_a_pending_effect_never_authorizes_retry()
    {
        var run=Create();Assert.True(run.RequestRetry(Now).IsFailure);
        var lease=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        Assert.True(run.RequestRetry(Now).IsFailure);Assert.False(run.RetryRequested);
        run.RecordOutcome(lease.Fence,Now,FormActionRunState.Uncertain,"unknown",false);
        Assert.True(run.RequestConsult(Now).IsSuccess);
        Assert.True(run.RequestRetry(Now).IsFailure);Assert.False(run.RetryRequested);
        var consult=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(consult.Fence,Now,FormActionRunState.Running,null,false);
        Assert.True(run.RequestRetry(Now).IsFailure);Assert.False(run.RetryRequested);
    }
    [Fact]
    public void Repeating_an_accepted_retry_preserves_the_running_reservation()
    {
        var run=Create();var lease=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;run.RecordOutcome(lease.Fence,Now,FormActionRunState.Failed,"failed",true);
        Assert.True(run.RequestRetry(Now).IsSuccess);Assert.True(run.RequestRetry(Now).IsSuccess);
        var retry=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;var expires=run.LeaseExpiresAtUtc;
        Assert.True(run.RequestRetry(Now.AddSeconds(1)).IsSuccess);Assert.Equal(expires,run.LeaseExpiresAtUtc);Assert.Equal(retry.Fence,run.Fence);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_delivery_review_reserves_reconciliation_and_keeps_human_evidence_after_completion(bool wasSent)
    {
        var run=Create();var actor=Guid.NewGuid();var first=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(first.Fence,Now,FormActionRunState.Uncertain,"unknown",false);
        Assert.True(run.RequestDeliveryReview(Recipient,run.Fence,wasSent,actor,Now.AddMinutes(1)).IsSuccess);
        Assert.True(run.DeliveryReviewRequested);Assert.False(run.RetryRequested);
        Assert.Equal(wasSent,run.DeliveryReviewWasSent);Assert.Equal(actor,run.DeliveryReviewedByUserId);
        Assert.Equal(Now.AddMinutes(1),run.DeliveryReviewedAtUtc);
        var review=run.Reserve(Now.AddMinutes(1),TimeSpan.FromMinutes(2)).Value;
        Assert.True(review.ReconciliationOnly);
        run.RecordOutcome(review.Fence,Now.AddMinutes(1),wasSent?FormActionRunState.Completed:FormActionRunState.Failed,null,!wasSent);
        Assert.False(run.DeliveryReviewRequested);Assert.Equal(wasSent,run.DeliveryReviewWasSent);
        Assert.Equal(actor,run.DeliveryReviewedByUserId);Assert.Equal(Now.AddMinutes(1),run.DeliveryReviewedAtUtc);
    }
    [Fact]
    public void A_lost_review_response_can_be_replayed_after_the_worker_consumed_the_decision()
    {
        var run=Create();var actor=Guid.NewGuid();var first=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(first.Fence,Now,FormActionRunState.Uncertain,"unknown",false);
        Assert.True(run.RequestDeliveryReview(Recipient,first.Fence,true,actor,Now.AddMinutes(1)).IsSuccess);
        var review=run.Reserve(Now.AddMinutes(1),TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(review.Fence,Now.AddMinutes(1),FormActionRunState.Completed,null,false);
        Assert.True(run.RequestDeliveryReview(Recipient,first.Fence,true,actor,Now.AddMinutes(2)).IsSuccess);
        Assert.Equal(FormActionRunState.Completed,run.State);Assert.False(run.DeliveryReviewRequested);
        Assert.Equal(Now.AddMinutes(1),run.DeliveryReviewedAtUtc);
    }
    [Fact]
    public void A_review_for_an_old_attempt_cannot_reopen_or_change_a_later_uncertainty()
    {
        var run=Create();var actor=Guid.NewGuid();var first=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(first.Fence,Now,FormActionRunState.Uncertain,"unknown",false);
        run.RequestDeliveryReview(Recipient,first.Fence,false,actor,Now);var reviewed=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(reviewed.Fence,Now,FormActionRunState.Failed,"not-sent",true);run.RequestRetry(Now);
        var retry=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;run.RecordOutcome(retry.Fence,Now,FormActionRunState.Uncertain,"unknown-again",false);
        Assert.True(run.RequestDeliveryReview(Recipient,first.Fence,false,actor,Now.AddMinutes(1)).IsSuccess);
        Assert.False(run.DeliveryReviewRequested);Assert.Equal(FormActionRunState.Uncertain,run.State);
        Assert.True(run.RequestDeliveryReview(Recipient,first.Fence,true,actor,Now).IsFailure);
        Assert.True(run.RequestDeliveryReview(Recipient,retry.Fence,true,actor,Now.AddMinutes(2)).IsSuccess);
        Assert.Equal(retry.Fence,run.DeliveryReviewExpectedRevision);
        Assert.True(run.RequestDeliveryReview(Recipient,first.Fence,false,actor,Now).IsFailure);
        Assert.True(run.DeliveryReviewWasSent);Assert.Equal(Now.AddMinutes(2),run.DeliveryReviewedAtUtc);
    }
    [Fact]
    public void A_pdf_or_an_unfinished_notification_cannot_be_reviewed_as_a_message()
    {
        var run=Create();Assert.True(run.RequestDeliveryReview(Recipient,run.Fence,true,Guid.NewGuid(),Now).IsFailure);
        var pdf=BusinessFormActionRun.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
            "Approved","GeneratePdf",0,null,"{}",true,Now);
        var lease=pdf.Reserve(Now,TimeSpan.FromMinutes(2)).Value;pdf.RecordOutcome(lease.Fence,Now,FormActionRunState.Uncertain,"unknown",false);
        Assert.True(pdf.RequestDeliveryReview(Recipient,pdf.Fence,false,Guid.NewGuid(),Now).IsFailure);Assert.False(pdf.DeliveryReviewRequested);
    }
    [Fact]
    public void A_delivery_review_replay_is_bound_to_its_original_recipient()
    {
        var run=Create();var actor=Guid.NewGuid();var lease=run.Reserve(Now,TimeSpan.FromMinutes(2)).Value;
        run.RecordOutcome(lease.Fence,Now,FormActionRunState.Uncertain,"unknown",false);
        Assert.True(run.RequestDeliveryReview(Recipient,lease.Fence,true,actor,Now).IsSuccess);
        Assert.True(run.RequestDeliveryReview(Guid.NewGuid(),lease.Fence,true,actor,Now).IsFailure);
        Assert.Equal(Recipient,run.DeliveryReviewRecipientUserId);
    }
    private static BusinessFormActionRun Create() => BusinessFormActionRun.Create(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),
        Guid.NewGuid(),Guid.NewGuid(),"Received","NotifyAuthor",0,null,"{}",true,Now);
}
