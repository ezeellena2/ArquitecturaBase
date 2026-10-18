using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;
public sealed class WhatsAppAudioDispatchServiceTests
{
    [Fact]
    public async Task Acquires_outside_the_transaction_and_seals_one_request_before_the_model_effect()
    {
        var f=new WhatsAppAudioFixture();f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var result=await f.Dispatch.PrepareAsync(f.Contact.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Equal(1,f.Downloads);Assert.Equal(1,f.UnitOfWork.Transactions);
        Assert.Equal(["contact","message"],f.Locks);Assert.Equal(f.Message.AudioRequestId,result.Value.RequestId);Assert.Equal("",result.Value.Operation!.Text);
        Assert.NotNull(result.Value.Operation!.Audio);Assert.Equal(47,result.Value.Operation!.Audio.Size);Assert.Equal(f.Subject,result.Value.Operation!.Context.ActorUserId);
        Assert.Equal(WhatsAppAudioFixture.Now.AddDays(90),result.Value.Operation!.ContentExpiresAtUtc);Assert.Empty(result.Value.Operation!.Context.AllowedCapabilities);
    }
    [Fact]
    public async Task Recovers_private_bytes_after_a_crash_without_downloading_or_changing_the_request()
    {
        var f=new WhatsAppAudioFixture();var id=f.Pin();await f.Files.WriteOnceAsync(ArquitecturaBase.Application.Modules.WhatsApp.Services.WhatsAppAudioStoragePolicy.Key(f.Message.Id),new MemoryStream(WhatsAppAudioFixture.Bytes()),"audio/ogg",TestContext.Current.CancellationToken);
        var result=await f.Dispatch.PrepareAsync(f.Contact.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.NotNull(result.Value);Assert.Equal(id,result.Value.RequestId);Assert.Equal(0,f.Downloads);Assert.Equal(1,f.Files.WriteCount);
    }
    [Fact]
    public async Task Manual_audio_without_an_explicit_request_does_not_start_automatically()
    {
        var f=new WhatsAppAudioFixture();var result=await f.Dispatch.PrepareAsync(f.Contact.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Null(result.Value);Assert.Equal(0,f.Downloads);Assert.Null(f.Message.AudioRequestId);
    }
    [Fact]
    public async Task Taking_manual_while_downloading_preserves_audio_without_an_automatic_model_request()
    {
        var f=new WhatsAppAudioFixture();f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);f.BeforeDownload=()=>f.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        var result=await f.Dispatch.PrepareAsync(f.Contact.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Null(result.Value);Assert.Null(f.Message.AudioRequestId);Assert.Equal(1,f.Files.WriteCount);
    }
    [Fact]
    public async Task Revoking_read_before_preparation_makes_zero_external_calls()
    {
        var f=new WhatsAppAudioFixture();var pinned=f.Pin();f.Permissions.Permissions[f.Actor]=[];
        var result=await f.Dispatch.PrepareAsync(f.Contact.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Null(result.Value);Assert.Equal(0,f.Downloads);
        Assert.Equal(WhatsAppAudioState.Unavailable,f.Message.AudioState);Assert.Equal(pinned,f.Message.AudioRequestId);
        Assert.Equal(["contact","message"],f.Locks);Assert.Equal(1,f.UnitOfWork.Commits);
    }
    [Fact]
    public async Task A_definitive_media_failure_is_durable_and_does_not_call_the_model()
    {
        var f=new WhatsAppAudioFixture();f.Pin();f.DownloadError=FileErrors.HashMismatch;
        var result=await f.Dispatch.PrepareAsync(f.Contact.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Null(result.Value);Assert.Equal(WhatsAppAudioState.Unavailable,f.Message.AudioState);Assert.Equal(1,f.UnitOfWork.Commits);
    }
    [Fact]
    public async Task A_known_complete_result_stores_the_whole_original_transcript()
    {
        var f=new WhatsAppAudioFixture();var id=f.Pin();f.Message.StoreAudio("private-key",47);
        var result=await f.Dispatch.CompleteAsync(f.Contact.Id,id,Result.Success(new AssistantOperationResponse(id,"completed",null,null,null){NormalizedAudio=new("Visité el local y saqué una foto.",["es"])}),TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAudioState.Completed,f.Message.AudioState);Assert.Equal("Visité el local y saqué una foto.",f.Message.Body);Assert.Equal(WhatsAppMessageKind.Audio,f.Message.Kind);Assert.Equal(1,f.UnitOfWork.Commits);
    }
    [Fact]
    public async Task Uncertain_result_is_terminal_and_never_gets_another_request()
    {
        var f=new WhatsAppAudioFixture();var id=f.Pin();
        Assert.True((await f.Dispatch.CompleteAsync(f.Contact.Id,id,Result.Success(new AssistantOperationResponse(id,"uncertain",null,null,"unknown")),TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(WhatsAppAudioState.Uncertain,f.Message.AudioState);Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id,TestContext.Current.CancellationToken)).Value);Assert.Equal(id,f.Message.AudioRequestId);Assert.Equal(0,f.Downloads);
    }
    [Fact]
    public async Task Transient_capacity_or_active_work_keeps_the_audio_request_for_a_later_retry()
    {
        var f = new WhatsAppAudioFixture();
        var id = f.Pin();
        Assert.True((await f.Dispatch.PrepareAsync(f.Contact.Id, TestContext.Current.CancellationToken)).IsSuccess);
        var transient = WhatsAppChatErrors.RequestConflict with { Metadata = new Dictionary<string, object?> { ["retryable"] = true } };
        var result = await f.Dispatch.CompleteAsync(f.Contact.Id, id, Result.Failure<AssistantOperationResponse>(transient), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppAudioState.Transcribing, f.Message.AudioState);
        Assert.Equal(id, f.Message.AudioRequestId);
        var retry = await f.Dispatch.PrepareAsync(f.Contact.Id, TestContext.Current.CancellationToken);
        Assert.True(retry.IsSuccess);
        Assert.NotNull(retry.Value);
        Assert.Equal(id, retry.Value.RequestId);
        Assert.Null(f.Message.Body);
    }

    [Fact]
    public async Task Submit_failure_does_not_leave_a_pending_message_forever()
    {
        var f=new WhatsAppAudioFixture();var id=f.Pin();
        await f.Dispatch.CompleteAsync(f.Contact.Id,id,Result.Failure<AssistantOperationResponse>(WhatsAppChatErrors.Forbidden),TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppAudioState.Unavailable,f.Message.AudioState);Assert.Null(f.Message.Body);
    }
    [Fact]
    public async Task A_late_result_cannot_restore_expired_audio_text()
    {
        var f=new WhatsAppAudioFixture();var id=f.Pin();f.Message.StoreAudio("private-key",47);f.Clock.Advance(TimeSpan.FromDays(91));
        await f.Dispatch.CompleteAsync(f.Contact.Id,id,Result.Success(new AssistantOperationResponse(id,"completed",null,null,null){NormalizedAudio=new("Late",[])}),TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppAudioState.Expired,f.Message.AudioState);Assert.Null(f.Message.Body);
    }
    [Fact]
    public async Task A_result_for_another_request_cannot_complete_the_audio()
    {
        var f=new WhatsAppAudioFixture();var id=f.Pin();f.Message.StoreAudio("private-key",47);
        var result=await f.Dispatch.CompleteAsync(f.Contact.Id,id,Result.Success(new AssistantOperationResponse(Guid.NewGuid(),"completed",null,null,null){NormalizedAudio=new("Wrong",[])}),TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);Assert.Null(f.Message.Body);Assert.Equal(WhatsAppAudioState.Pending,f.Message.AudioState);
    }
    [Fact]
    public async Task Revocation_while_the_model_runs_prevents_publication()
    {
        var f=new WhatsAppAudioFixture();var id=f.Pin();f.Message.StoreAudio("private-key",47);f.Permissions.Permissions[f.Actor]=[];
        await f.Dispatch.CompleteAsync(f.Contact.Id,id,Result.Success(new AssistantOperationResponse(id,"completed",null,null,null){NormalizedAudio=new("Hidden",[])}),TestContext.Current.CancellationToken);
        Assert.Null(f.Message.Body);Assert.Equal(WhatsAppAudioState.Unavailable,f.Message.AudioState);
    }
    [Fact]
    public void Presentation_uses_source_expiry_and_hides_its_content_after_retention()
    {
        var f=new WhatsAppAudioFixture();f.Pin();f.Message.StoreAudio("private-key",47);
        Assert.Equal(WhatsAppAudioFixture.Now.AddDays(90),f.Presentation.ExpiresAtUtc(f.Row()));Assert.True(f.Presentation.View(f.Row()).CanPlay);
        f.Clock.Advance(TimeSpan.FromDays(90));Assert.True(f.Presentation.IsExpired(f.Row()));Assert.Equal(WhatsAppAudioState.Expired,f.Presentation.View(f.Row()).State);Assert.False(f.Presentation.View(f.Row()).CanPlay);
    }
}
