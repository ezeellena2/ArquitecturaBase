using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAudioQuotaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // O39 (2026-11-21): the offers of a day only stop offering forms, so an automatic audio is transcribed whatever the quota.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_full_offer_quota_still_transcribes_an_automatic_audio(bool copied)
    {
        var f = new WhatsAppAudioFixture();
        f.Offers.OfferCount = 2;
        if (copied) Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
        f.Contact.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.RecoverOnly);
        Assert.Equal(WhatsAppAudioState.Transcribing, f.Message.AudioState);
        Assert.Equal(result.Value.RequestId, f.Message.AudioRequestId);
        Assert.Equal("whatsapp-assistant", result.Value.Operation!.Context.ChannelId);
        Assert.Null(f.Offers.LastCount);
        Assert.Equal(1, f.Downloads);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task A_pinned_automatic_request_goes_on_whatever_the_offer_quota(int offers)
    {
        var f = new WhatsAppAudioFixture();
        f.Contact.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);
        var requestId = f.Pin(automatic: true);
        Assert.True(f.Message.StoreAudio(WhatsAppAudioStoragePolicy.Key(f.Message.Id), 47).IsSuccess);
        f.Message.SetAudioState(WhatsAppAudioState.Transcribing);
        f.Offers.OfferCount = offers;

        var result = await f.Dispatch.PrepareAsync(f.Contact.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(requestId, result.Value.RequestId);
        Assert.False(result.Value.RecoverOnly);
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
        if (assistantMode) f.Contact.ChangeMode(WhatsAppConversationMode.Assistant, null, DateTime.UnixEpoch);

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
