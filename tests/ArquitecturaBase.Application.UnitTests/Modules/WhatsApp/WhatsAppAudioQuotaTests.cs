using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAudioQuotaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exhausted_automatic_quota_keeps_a_playable_copy_and_finishes_without_a_new_request(bool copied)
    {
        var f = new WhatsAppAudioFixture();
        f.Offers.OfferCount = 2;
        if (copied) Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
        f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(WhatsAppAudioState.Unavailable, f.Message.AudioState);
        Assert.Equal(WhatsAppAssistantErrors.Unavailable.Code, f.Message.AudioFailureCode);
        Assert.Null(f.Message.AudioRequestId);
        Assert.Null(f.Message.AudioActorUserId);
        Assert.Null(f.Message.AudioContextJson);
        Assert.Null(f.Message.Body);
        Assert.True(f.Presentation.View(f.Row()).CanPlay);
        Assert.Equal(1, f.Downloads);
        Assert.Equal(1, f.Files.WriteCount);
        var reads = f.Files.ReadCount;
        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
        Assert.Equal(1, f.Downloads);
        Assert.Equal(reads, f.Files.ReadCount);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task A_pinned_automatic_request_is_recovery_only_when_the_offer_quota_is_exhausted(int offers, bool recoverOnly)
    {
        var f = new WhatsAppAudioFixture();
        f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var requestId = f.Pin(automatic: true);
        Assert.True(f.Message.StoreAudio(WhatsAppAudioStoragePolicy.Key(f.Message.Id), 47).IsSuccess);
        f.Message.SetAudioState(WhatsAppAudioState.Transcribing);
        f.Offers.OfferCount = offers;

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(requestId, result.Value.RequestId);
        Assert.Equal(recoverOnly, result.Value.RecoverOnly);
        Assert.Equal(WhatsAppAudioState.Transcribing, f.Message.AudioState);
        Assert.Null(f.Message.Body);
        Assert.Equal(0, f.Downloads);
        Assert.Equal(0, f.Files.ReadCount);
        Assert.Equal(0, f.Files.WriteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_captured_manual_normalization_is_independent_of_automatic_offer_quota_and_current_mode(bool assistantMode)
    {
        var f = new WhatsAppAudioFixture();
        var requestId = f.Pin();
        Assert.True(f.Message.StoreAudio(WhatsAppAudioStoragePolicy.Key(f.Message.Id), 47).IsSuccess);
        f.Message.SetAudioState(WhatsAppAudioState.Transcribing);
        f.Offers.OfferCount = 2;
        if (assistantMode) f.Contact.ChangeMode(WhatsAppConversationMode.Assistant);

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(requestId, result.Value.RequestId);
        Assert.False(result.Value.RecoverOnly);
        Assert.Equal("whatsapp-manual", result.Value.Operation!.Context.ChannelId);
        Assert.Equal(f.Actor, result.Value.Operation.Context.ActorUserId);
        Assert.Null(f.Offers.LastCount);
        Assert.Equal(0, f.Downloads);
    }
}
