using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppAudioLimitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_normalization_preserves_playable_private_audio_without_creating_model_work(bool manual)
    {
        var bytes = new byte[1_000_001];
        WhatsAppAudioFixture.Bytes().CopyTo(bytes, 0);
        var fixture = new WhatsAppAudioFixture(bytes);
        if (manual) fixture.Pin();
        else fixture.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var result = await fixture.Dispatch.PrepareAsync(fixture.Contact.Id, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(WhatsAppAudioState.Unavailable, fixture.Message.AudioState);
        Assert.Equal(FileErrors.TooLarge.Code, fixture.Message.AudioFailureCode);
        Assert.Equal(bytes.Length, fixture.Message.AudioSize);
        Assert.True(fixture.Presentation.View(fixture.Row()).CanPlay);
        Assert.Equal(1, fixture.Files.WriteCount);
    }
    [Fact]
    public async Task Audio_at_the_normalization_byte_limit_can_be_accepted_without_changing_the_STT_profile()
    {
        var bytes = new byte[1_000_000];
        WhatsAppAudioFixture.Bytes().CopyTo(bytes, 0);
        var fixture = new WhatsAppAudioFixture(bytes);
        fixture.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var result = await fixture.Dispatch.PrepareAsync(fixture.Contact.Id, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(bytes.Length, result.Value.Operation!.Audio!.Size);
        Assert.Equal(WhatsAppAudioState.Transcribing, fixture.Message.AudioState);
    }
}
