using System.Text.Json;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAudioManualLimitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_known_oversized_manual_audio_returns_its_public_reason_and_playable_copy_without_pinning_work(bool alreadyUnavailable)
    {
        var bytes = new byte[1_000_001];
        WhatsAppAudioFixture.Bytes().CopyTo(bytes, 0);
        var f = new WhatsAppAudioFixture(bytes);
        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);
        if (alreadyUnavailable) f.Message.SetAudioState(WhatsAppAudioState.Unavailable, FileErrors.TooLarge.Code);
        var reads = f.Files.ReadCount;
        var downloads = f.Downloads;
        var writes = f.Files.WriteCount;

        var result = await f.Service.RequestAsync(f.Contact.Id, f.Message.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppAudioState.Unavailable, result.Value.State);
        Assert.True(result.Value.CanPlay);
        Assert.Equal(WhatsAppAudioState.Unavailable, f.Message.AudioState);
        Assert.Equal(FileErrors.TooLarge.Code, f.Message.AudioFailureCode);
        var body = JsonSerializer.SerializeToElement(result.Value, JsonSerializerOptions.Web);
        Assert.True(body.TryGetProperty("reasonCode", out var reason), "The audio response must expose the known unavailability reason.");
        Assert.Equal("tooLarge", reason.GetString());
        Assert.Null(f.Message.AudioRequestId);
        Assert.Null(f.Message.AudioActorUserId);
        Assert.Null(f.Message.AudioContextJson);
        Assert.Null(f.Message.AudioContentExpiresAtUtc);
        Assert.Null(f.Offers.LastCount);
        Assert.Equal(reads, f.Files.ReadCount);
        Assert.Equal(downloads, f.Downloads);
        Assert.Equal(writes, f.Files.WriteCount);
    }

    [Fact]
    public async Task The_exact_known_manual_byte_limit_still_pins_one_explicit_normalization()
    {
        var bytes = new byte[1_000_000];
        WhatsAppAudioFixture.Bytes().CopyTo(bytes, 0);
        var f = new WhatsAppAudioFixture(bytes);
        Assert.Null((await f.Dispatch.PrepareAsync(f.Contact.Id, Ct)).Value);

        var result = await f.Service.RequestAsync(f.Contact.Id, f.Message.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppAudioState.Pending, result.Value.State);
        Assert.True(result.Value.CanPlay);
        Assert.NotNull(f.Message.AudioRequestId);
        Assert.NotNull(f.Message.AudioContextJson);
        Assert.Equal(f.Actor, f.Message.AudioActorUserId);
    }

    [Fact]
    public async Task A_previously_pinned_manual_request_replays_even_if_its_known_size_exceeds_the_new_limit()
    {
        var f = new WhatsAppAudioFixture();
        var requestId = f.Pin();
        var context = f.Message.AudioContextJson;
        Assert.True(f.Message.StoreAudio("whatsapp/audio/previously-accepted.ogg", 1_000_001).IsSuccess);
        f.Message.SetAudioState(WhatsAppAudioState.Transcribing);

        var result = await f.Service.RequestAsync(f.Contact.Id, f.Message.Id, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(WhatsAppAudioState.Transcribing, result.Value.State);
        Assert.True(result.Value.CanPlay);
        Assert.Equal(requestId, f.Message.AudioRequestId);
        Assert.Equal(context, f.Message.AudioContextJson);
        Assert.Null(f.Message.AudioFailureCode);
        Assert.Equal(0, f.Downloads);
        Assert.Equal(0, f.Files.ReadCount);
        Assert.Equal(0, f.Files.WriteCount);
    }
}
