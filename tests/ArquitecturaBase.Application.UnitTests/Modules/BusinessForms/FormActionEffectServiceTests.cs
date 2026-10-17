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
        var work=Work(reconcile:true,people:[accepted,uncertain]) with{DeliveryReview=evidence};
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
    private static FormActionWorkResponse Work(string kind="NotifyAuthor",bool reconcile=false,bool retry=false,IReadOnlyList<Guid>? people=null)
    {
        var fixture=new FormActionFixture();var action=FormActionFixture.Action(kind);var captured=fixture.Capture([new(Guid.NewGuid(),"Received",[],[action])]);var runId=Guid.NewGuid();
        var pdf=kind=="GeneratePdf"?new FormPdfActionSnapshotResponse(runId,null,0,new(new Dictionary<string,string>{{"es","PDF"}},[],true,null,true),["es","en"],"Arquitectura Base",captured.Snapshot.Response.Actor,captured.Snapshot.Decision):null;
        return new(runId,captured.Record.Id,kind,1,reconcile,retry,new(null,pdf,null,(people??[]).Select(id=>new FormActionRecipient(id,"es","Private body")).ToArray()),[]);
    }
}
