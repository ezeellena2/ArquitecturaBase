using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormActionEffectServiceTests
{
    [Fact]
    public async Task A_confirmed_recipient_is_never_sent_again_when_another_recipient_is_retried()
    {
        var first=Guid.NewGuid();var second=Guid.NewGuid();var channel=new FakeFormActionChannel();
        channel.Statuses[first]=new(StructuredRecordActionState.Accepted);channel.Statuses[second]=new(StructuredRecordActionState.Failed,"temporary",true);
        var work=Work(reconcile:true,retry:true,people:[first,second]);var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var result=await service.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,result.State);Assert.Empty(channel.Messages);Assert.Equal(new[]{second},channel.Retries);Assert.Equal(new[]{first,second},channel.Reads);Assert.Equal(work.Fence,Assert.Single(channel.ControlRevisions));
    }
    [Fact]
    public async Task Uncertainty_is_queried_before_retry_and_never_authorizes_another_send()
    {
        var person=Guid.NewGuid();var channel=new FakeFormActionChannel();channel.Statuses[person]=new(StructuredRecordActionState.Uncertain);
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var result=await service.ExecuteAsync(Work(reconcile:true,retry:true,people:[person]),CancellationToken.None);
        Assert.Equal(FormActionRunState.Uncertain,result.State);Assert.Single(channel.Reads);Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);
    }
    [Fact]
    public async Task A_consult_only_reads_and_a_retry_sends_only_after_absence_is_confirmed()
    {
        var channel=new FakeFormActionChannel();var work=Work(reconcile:true,people:[Guid.NewGuid()]);
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var observed=await service.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(FormActionRunState.Failed,observed.State);Assert.True(observed.CanRetry);Assert.Empty(channel.Messages);
        var resumed=await service.ExecuteAsync(work with{RetryRequested=true},CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,resumed.State);Assert.Single(channel.Messages);Assert.Equal(2,channel.Reads.Count);
    }
    [Fact]
    public async Task Pdf_retry_inspects_uploaded_copies_before_generating_only_the_missing_cultures()
    {
        var pdf=new FakeFormPdfActionService();var artifact=new FormPdfArtifact("es","private/original.pdf","copy.pdf","application/pdf",10,"hash");
        pdf.Outcomes.Enqueue(new(FormPdfActionState.NotFound,[artifact]));
        pdf.Outcomes.Enqueue(new(FormPdfActionState.Completed,[artifact,artifact with{Culture="en",StorageKey="private/english.pdf"}]));
        var service=new FormActionEffectService(pdf,[],NullLogger<FormActionEffectService>.Instance);
        var result=await service.ExecuteAsync(Work("GeneratePdf",true,true),CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,result.State);Assert.Equal(new[]{true,false},pdf.Executed);Assert.Equal(2,result.Pdf!.Artifacts.Count);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Human_review_resolves_only_uncertain_recipients_without_enqueuing_or_retrying(bool wasSent)
    {
        var accepted=Guid.NewGuid();var uncertain=Guid.NewGuid();var channel=new FakeFormActionChannel();
        channel.Statuses[accepted]=new(StructuredRecordActionState.Accepted);channel.Statuses[uncertain]=new(StructuredRecordActionState.Uncertain);
        var evidence=new StructuredRecordActionReview(1,wasSent,Guid.NewGuid(),new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc));
        var work=Work(reconcile:true,people:[accepted,uncertain]) with{DeliveryReview=evidence,DeliveryReviewRecipientUserId=uncertain};
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var result=await service.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(wasSent?FormActionRunState.Completed:FormActionRunState.Failed,result.State);
        Assert.Equal(!wasSent,result.CanRetry);Assert.Equal((uncertain,evidence),Assert.Single(channel.Reviews));
        Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);
    }
    [Fact]
    public async Task A_technical_failure_keeps_uncertainty_and_logs_only_the_exception_type()
    {
        var channel=new FakeFormActionChannel{ReadException=new IOException("secret-storage-key-and-private-answer")};
        var logger=new FormActionCaptureLogger<FormActionEffectService>();var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],logger);
        var result=await service.ExecuteAsync(Work(people:[Guid.NewGuid()]),CancellationToken.None);
        Assert.Equal(FormActionRunState.Uncertain,result.State);Assert.Contains(logger.Entries,text=>text.Contains("IOException",StringComparison.Ordinal));
        Assert.Contains(logger.Entries,text=>text.Contains(FormActionErrors.EffectUnconfirmed.Code,StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Entries,text=>text.Contains("secret-storage-key",StringComparison.Ordinal));Assert.Empty(channel.Messages);
    }
    [Fact]
    public void Duplicate_channels_fail_configuration_before_any_effect()=>
        Assert.Throws<InvalidOperationException>(()=>new FormActionEffectService(new FakeFormPdfActionService(),[new FakeFormActionChannel(),new FakeFormActionChannel()],NullLogger<FormActionEffectService>.Instance));
    [Fact]
    public async Task Role_notification_completes_after_known_individual_failures_without_authorizing_partial_retry()
    {
        var accepted=Guid.NewGuid();var failed=Guid.NewGuid();var channel=new FakeFormActionChannel();
        channel.Statuses[accepted]=new(StructuredRecordActionState.Accepted);channel.Statuses[failed]=new(StructuredRecordActionState.Failed,"recipient-unavailable",true);
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var outcome=await service.ExecuteAsync(Work("NotifyRole",people:[accepted,failed]),CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,outcome.State);Assert.False(outcome.CanRetry);Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);
    }
    [Fact]
    public async Task An_empty_role_completes_without_requiring_an_installed_channel()
    {
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[],NullLogger<FormActionEffectService>.Instance);
        var outcome=await service.ExecuteAsync(Work("NotifyRole"),CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,outcome.State);Assert.False(outcome.CanRetry);
    }
    [Fact]
    public async Task An_accepted_outcome_is_kept_even_if_the_channel_no_longer_finds_its_row()
    {
        var accepted=Guid.NewGuid();var pending=Guid.NewGuid();var channel=new FakeFormActionChannel();
        channel.Statuses[pending]=new(StructuredRecordActionState.Failed,"recipient-unavailable",true);
        var work=Work("NotifyRole",reconcile:true,people:[accepted,pending]) with{PriorRecipients=[new(accepted,"Accepted employee","phone",new(StructuredRecordActionState.Accepted))]};
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var outcome=await service.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,outcome.State);Assert.Equal(new[]{pending},channel.Reads);Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);
        Assert.Equal(StructuredRecordActionState.Accepted,Assert.Single(outcome.Recipients!,recipient=>recipient.UserId==accepted).Status.State);
    }
    [Fact]
    public async Task A_role_recipient_with_a_technical_failure_does_not_prevent_other_recipients_from_being_sent()
    {
        var uncertain=Guid.NewGuid();var available=Guid.NewGuid();var channel=new FakeFormActionChannel();channel.ReadExceptions[uncertain]=new IOException("private-provider-detail");
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var outcome=await service.ExecuteAsync(Work("NotifyRole",people:[uncertain,available]),CancellationToken.None);
        Assert.Equal(FormActionRunState.Uncertain,outcome.State);Assert.Equal(available,Assert.Single(channel.Messages).RecipientUserId);
        Assert.Equal(2,outcome.Recipients!.Count);
    }
    [Fact]
    public async Task A_known_role_failure_keeps_its_original_reason_while_other_recipients_are_pending()
    {
        var failed=Guid.NewGuid();var pending=Guid.NewGuid();var channel=new FakeFormActionChannel();channel.Statuses[pending]=new(StructuredRecordActionState.Pending);
        var work=Work("NotifyRole",reconcile:true,people:[failed,pending]) with{PriorRecipients=[new(failed,"Employee","phone",new(StructuredRecordActionState.Failed,"WhatsApp.FormAction.WindowClosed",true))]};
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var result=await service.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(FormActionRunState.Running,result.State);Assert.Equal(new[]{pending},channel.Reads);
        Assert.Equal("WhatsApp.FormAction.WindowClosed",Assert.Single(result.Recipients!,recipient=>recipient.UserId==failed).Status.FailureCode);
        Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);
    }
    [Fact]
    public async Task A_role_recipient_reviewed_as_not_sent_can_still_observe_a_late_acceptance_without_resending()
    {
        var reviewed=Guid.NewGuid();var pending=Guid.NewGuid();var evidence=new StructuredRecordActionReview(1,false,Guid.NewGuid(),new DateTime(2026,10,17,12,0,0,DateTimeKind.Utc));
        var channel=new FakeFormActionChannel();channel.Statuses[reviewed]=new(StructuredRecordActionState.Accepted,Review:evidence);channel.Statuses[pending]=new(StructuredRecordActionState.Accepted);
        var work=Work("NotifyRole",reconcile:true,people:[reviewed,pending]) with{PriorRecipients=[new(reviewed,"Employee","phone",new(StructuredRecordActionState.Failed,"WhatsApp.FormAction.ReviewedNotSent",true,Review:evidence))]};
        var service=new FormActionEffectService(new FakeFormPdfActionService(),[channel],NullLogger<FormActionEffectService>.Instance);
        var result=await service.ExecuteAsync(work,CancellationToken.None);
        Assert.Equal(FormActionRunState.Completed,result.State);Assert.Equal(new[]{reviewed,pending},channel.Reads);
        var accepted=Assert.Single(result.Recipients!,recipient=>recipient.UserId==reviewed);Assert.Equal(StructuredRecordActionState.Accepted,accepted.Status.State);Assert.Equal(evidence,accepted.Status.Review);
        Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);
    }
    private static FormActionWorkResponse Work(string kind="NotifyAuthor",bool reconcile=false,bool retry=false,IReadOnlyList<Guid>? people=null)
    {
        var fixture=new FormActionFixture();var action=FormActionFixture.Action(kind);var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[action])]);var runId=Guid.NewGuid();
        var pdf=kind=="GeneratePdf"?new FormPdfActionSnapshotResponse(runId,null,0,new(new Dictionary<string,string>{{"es","PDF"}},[],true,null,true),["es","en"],"Arquitectura Base",captured.Snapshot.Response.Actor,captured.Snapshot.Decision):null;
        return new(runId,captured.Record.Id,kind,1,reconcile,retry,new(null,pdf,null,(people??[]).Select(id=>new FormActionRecipient(id,"es","Private body")).ToArray()),[]);
    }
}
