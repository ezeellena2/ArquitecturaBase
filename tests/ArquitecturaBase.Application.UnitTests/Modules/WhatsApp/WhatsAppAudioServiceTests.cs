using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;
public sealed class WhatsAppAudioServiceTests
{
    [Fact]
    public async Task Manual_post_pins_one_server_request_with_read_only_and_does_no_external_io()
    {
        var f=new WhatsAppAudioFixture();var result=await f.Service.RequestAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAudioState.Pending,result.Value.State);Assert.NotNull(f.Message.AudioRequestId);
        Assert.Equal(f.Actor,f.Message.AudioActorUserId);Assert.Equal(1,f.UnitOfWork.Commits);Assert.Equal(0,f.Downloads);Assert.Equal(0,f.Files.ReadCount);Assert.Equal(1,f.Signals);
    }
    [Fact]
    public async Task Repeated_posts_and_a_second_operator_share_the_same_normalization()
    {
        var f=new WhatsAppAudioFixture();Assert.True((await f.Service.RequestAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken)).IsSuccess);var id=f.Message.AudioRequestId;
        var second=f.Users.AddUser("second-audio-reader@example.test").Id;f.Permissions.Permissions[second]=[WhatsAppPermissions.Read];f.Current.UserId=second;
        Assert.True((await f.Service.RequestAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(id,f.Message.AudioRequestId);Assert.Equal(f.Actor,f.Message.AudioActorUserId);Assert.Equal(0,f.Downloads);
    }
    [Fact]
    public async Task Request_cannot_address_an_audio_in_another_contact()
    {
        var f=new WhatsAppAudioFixture();Assert.True((await f.Service.RequestAsync(Guid.NewGuid(),f.Message.Id,TestContext.Current.CancellationToken)).IsFailure);Assert.Null(f.Message.AudioRequestId);Assert.Equal(0,f.Signals);
    }
    [Fact]
    public async Task Revoked_read_cannot_pin_a_normalization()
    {
        var f=new WhatsAppAudioFixture();f.Permissions.Permissions[f.Actor]=[];
        Assert.True((await f.Service.RequestAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken)).IsFailure);Assert.Equal(0,f.UnitOfWork.Transactions);Assert.Null(f.Message.AudioRequestId);
    }
    [Fact]
    public async Task Expired_audio_is_not_restarted_by_opening_the_chat()
    {
        var f=new WhatsAppAudioFixture();f.Clock.Advance(TimeSpan.FromDays(91));var result=await f.Service.RequestAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAudioState.Expired,result.Value.State);Assert.Null(f.Message.AudioRequestId);Assert.Equal(0,f.Downloads);
    }
    [Fact]
    public async Task Private_playback_returns_verified_seekable_bytes_without_meta_or_a_model()
    {
        var f=new WhatsAppAudioFixture();await StoreAsync(f);var result=await f.Content.OpenAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);await using var content=result.Value.Content;Assert.True(content.CanSeek);Assert.Equal("audio/ogg",result.Value.ContentType);Assert.Equal(47,content.Length);Assert.Equal(0,f.Downloads);Assert.Equal(0,f.UnitOfWork.Transactions);
    }
    [Fact]
    public async Task Playback_rechecks_read_after_opening_private_bytes()
    {
        var f=new WhatsAppAudioFixture();await StoreAsync(f);f.Files.BeforeRead=()=>f.Permissions.Permissions[f.Actor]=[];
        Assert.True((await f.Content.OpenAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken)).IsFailure);Assert.True(f.Files.LastOpenedStream is null || !f.Files.LastOpenedStream.CanRead);
    }
    [Fact]
    public async Task Expired_playback_does_not_open_the_private_store()
    {
        var f=new WhatsAppAudioFixture();await StoreAsync(f);f.Clock.Advance(TimeSpan.FromDays(90));var before=f.Files.ReadCount;
        Assert.True((await f.Content.OpenAsync(f.Contact.Id,f.Message.Id,TestContext.Current.CancellationToken)).IsFailure);Assert.Equal(before,f.Files.ReadCount);
    }
    [Fact]
    public async Task Playback_from_another_contact_is_hidden_before_storage_io()
    {
        var f=new WhatsAppAudioFixture();await StoreAsync(f);var before=f.Files.ReadCount;
        Assert.True((await f.Content.OpenAsync(Guid.NewGuid(),f.Message.Id,TestContext.Current.CancellationToken)).IsFailure);Assert.Equal(before,f.Files.ReadCount);
    }
    private static async Task StoreAsync(WhatsAppAudioFixture f)
    {
        var stored=await new WhatsAppAudioStoragePolicy(f.Files).SaveAsync(f.Message.Id,WhatsAppAudioFixture.Bytes(),f.Message.AudioSha256!,TestContext.Current.CancellationToken);
        Assert.True(stored.IsSuccess);Assert.True(f.Message.StoreAudio(stored.Value.Key,stored.Value.Size).IsSuccess);
    }
}
