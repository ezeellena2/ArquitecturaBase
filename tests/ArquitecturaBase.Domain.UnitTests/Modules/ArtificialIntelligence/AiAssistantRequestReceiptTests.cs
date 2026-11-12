using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Domain.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantRequestReceiptTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_document_reading_receipt_keeps_its_channel_and_is_not_an_audio()
    {
        var receipt = AiAssistantRequestReceipt.Create(Guid.NewGuid(), Guid.NewGuid(), "fingerprint", "key", Guid.NewGuid(), Guid.NewGuid(),
            Now, Now.AddDays(30), "fixture-channel", false, isDocument: true);

        Assert.True(receipt.IsDocument);
        Assert.False(receipt.IsAudio);
        Assert.Equal("fixture-channel", receipt.ChannelId);
        Assert.Equal(DateOnly.FromDateTime(Now), receipt.AcceptedDayUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Receipts_of_interpretations_and_audio_are_not_documents(bool audio)
    {
        var receipt = AiAssistantRequestReceipt.Create(Guid.NewGuid(), Guid.NewGuid(), "fingerprint", "key", Guid.NewGuid(), Guid.NewGuid(),
            Now, Now.AddDays(30), "fixture-channel", audio);

        Assert.False(receipt.IsDocument);
        Assert.Equal(audio, receipt.IsAudio);
    }

    [Fact]
    public void A_receipt_is_never_an_audio_and_a_document_at_once()
    {
        Assert.Throws<ArgumentException>(() => AiAssistantRequestReceipt.Create(Guid.NewGuid(), Guid.NewGuid(), "fingerprint", "key",
            Guid.NewGuid(), Guid.NewGuid(), Now, Now.AddDays(30), "fixture-channel", true, isDocument: true));
    }
}
