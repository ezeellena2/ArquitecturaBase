using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Domain.UnitTests.Modules.WhatsApp;

public sealed class WhatsAppMessageAudioTests
{
    private static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    private static WhatsAppMessage Message() => WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.audio", WhatsAppMessageKind.Audio, null, null, Now);
    [Fact]
    public void New_audio_preserves_its_original_descriptor_without_reclassifying_historical_media()
    {
        var message = Message();
        Assert.True(message.SetAudioDescriptor("1234", "audio/ogg; codecs=opus", new string('a', 64)));
        Assert.Equal(WhatsAppAudioState.Pending, message.AudioState);
        Assert.Equal("1234", message.AudioMediaId);
        Assert.Equal("audio/ogg; codecs=opus", message.AudioContentType);
        Assert.Equal(new string('a', 64), message.AudioSha256);
        Assert.Null(message.Body);
        Assert.False(message.SetAudioDescriptor("9999", "audio/ogg", new string('b', 64)));
        Assert.Equal("1234", message.AudioMediaId);
        var historical = WhatsAppMessage.Inbound(Guid.NewGuid(), "wamid.old", WhatsAppMessageKind.Media, null, null, Now);
        Assert.Null(historical.AudioState);
    }
    [Theory]
    [InlineData(null, "audio/ogg", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("id-not-digits", "audio/ogg", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("123", "audio/mpeg", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("123", "audio/ogg", "bad")]
    public void Malformed_audio_remains_visible_but_unavailable(string? id, string mime, string hash)
    {
        var message = Message();
        Assert.False(message.SetAudioDescriptor(id, mime, hash));
        Assert.Equal(WhatsAppAudioState.Unavailable, message.AudioState);
        Assert.Equal(WhatsAppMessageKind.Audio, message.Kind);
        Assert.Null(message.AudioMediaId);
    }
    [Fact]
    public void First_real_actor_and_request_are_fixed_once_and_other_operators_share_the_state()
    {
        var message = Message(); message.SetAudioDescriptor("123", "audio/ogg; codecs=opus", new string('a', 64));
        var actor = Guid.NewGuid();
        var first = message.RequestAudioNormalization(actor, "context", 7, Now.AddDays(1), Now);
        Assert.True(first.IsSuccess);
        var next = message.RequestAudioNormalization(Guid.NewGuid(), "other-context", 8, Now.AddDays(2), Now);
        Assert.True(next.IsSuccess);
        Assert.Equal(first.Value, next.Value);
        Assert.Equal(actor, message.AudioActorUserId);
        Assert.Equal("context", message.AudioContextJson);
        Assert.Equal(Now.AddDays(1), message.AudioContentExpiresAtUtc);
        Assert.Equal(7, message.AudioLinkRevision);
    }
    [Fact]
    public void Completing_keeps_audio_kind_and_full_transcription_and_cannot_be_replaced()
    {
        var message = Message(); message.SetAudioDescriptor("123", "audio/ogg", new string('a', 64));
        var request = message.RequestAudioNormalization(Guid.NewGuid(), "context", 1, Now.AddDays(1), Now);
        Assert.True(request.IsSuccess);
        Assert.True(message.StoreAudio("private/audio", 42).IsSuccess);
        Assert.True(message.CompleteAudio(request.Value, "Fui a la sucursal Norte.", Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(WhatsAppAudioState.Completed, message.AudioState);
        Assert.Equal(WhatsAppMessageKind.Audio, message.Kind);
        Assert.Equal("Fui a la sucursal Norte.", message.Body);
        Assert.True(message.CompleteAudio(request.Value, "different", Now.AddMinutes(2)).IsFailure);
        Assert.Equal("Fui a la sucursal Norte.", message.Body);
    }
    [Theory]
    [InlineData("wrongRequest")]
    [InlineData("expired")]
    [InlineData("tooLong")]
    [InlineData("nul")]
    public void Invalid_or_late_transcripts_are_never_truncated_or_published(string failure)
    {
        var message = Message(); message.SetAudioDescriptor("123", "audio/ogg", new string('a', 64));
        var request = message.RequestAudioNormalization(Guid.NewGuid(), "context", 1, Now.AddHours(1), Now);
        Assert.True(request.IsSuccess);
        var text = failure == "tooLong" ? new string('a', 4001) : failure == "nul" ? "bad\0text" : "Full transcript";
        Assert.True(message.CompleteAudio(failure == "wrongRequest" ? Guid.NewGuid() : request.Value, text,
            failure == "expired" ? Now.AddHours(1) : Now.AddMinutes(1)).IsFailure);
        Assert.Null(message.Body);
    }
    [Fact]
    public void A_terminal_uncertain_result_cannot_start_another_normalization()
    {
        var message = Message(); message.SetAudioDescriptor("123", "audio/ogg", new string('a', 64));
        var first = message.RequestAudioNormalization(Guid.NewGuid(), "context", 1, Now.AddDays(1), Now);
        Assert.True(first.IsSuccess);
        message.SetAudioState(WhatsAppAudioState.Uncertain, "Ai.Assistant.ProviderUncertain");
        var replay = message.RequestAudioNormalization(Guid.NewGuid(), "context", 1, Now.AddDays(1), Now);
        Assert.True(replay.IsSuccess);
        Assert.Equal(first.Value, replay.Value);
        Assert.Equal(WhatsAppAudioState.Uncertain, message.AudioState);
    }

    [Fact]
    public void Acquisition_failures_saturate_at_five_but_keep_retrying_for_at_least_fifteen_minutes()
    {
        var message=Message();message.SetAudioDescriptor("123","audio/ogg",new string('a',64));
        for(var i=0;i<8;i++)Assert.False(message.RegisterAudioFailure(Now,"Files.Content.Unavailable"));
        Assert.Equal(5,message.AudioFailureCount);Assert.Equal(Now,message.AudioFirstFailureAtUtc);Assert.Equal(WhatsAppAudioState.Pending,message.AudioState);
        Assert.True(message.RegisterAudioFailure(Now.AddMinutes(15),"Files.Content.Unavailable"));Assert.Equal(WhatsAppAudioState.Unavailable,message.AudioState);Assert.Equal(5,message.AudioFailureCount);
    }
    [Fact]
    public void Acquisition_retries_need_both_the_count_and_the_age_limits()
    {
        var message=Message();message.SetAudioDescriptor("123","audio/ogg",new string('a',64));
        Assert.False(message.RegisterAudioFailure(Now,"Files.Content.Unavailable"));
        for(var i=0;i<3;i++)Assert.False(message.RegisterAudioFailure(Now.AddHours(1),"Files.Content.Unavailable"));
        Assert.True(message.RegisterAudioFailure(Now.AddHours(1),"Files.Content.Unavailable"));Assert.Equal(5,message.AudioFailureCount);
    }
    [Fact]
    public void Terminal_audio_does_not_restart_acquisition_or_advance_failure_metadata()
    {
        var message=Message();message.SetAudioDescriptor("123","audio/ogg",new string('a',64));message.SetAudioState(WhatsAppAudioState.Uncertain);
        Assert.False(message.RegisterAudioFailure(Now,"Files.Content.Unavailable"));Assert.Equal(0,message.AudioFailureCount);Assert.Null(message.AudioFirstFailureAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Successful_audio_storage_clears_the_acquisition_failure_window_including_an_idempotent_recovery(bool alreadyStored)
    {
        var message = Message();
        Assert.True(message.SetAudioDescriptor("123", "audio/ogg", new string('a', 64)));
        if (alreadyStored) Assert.True(message.StoreAudio("private/audio", 47).IsSuccess);
        for (var i = 0; i < 3; i++) Assert.False(message.RegisterAudioFailure(Now, "Files.Content.Unavailable"));
        Assert.Equal(3, message.AudioFailureCount);

        Assert.True(message.StoreAudio("private/audio", 47).IsSuccess);

        Assert.Equal(0, message.AudioFailureCount);
        Assert.Null(message.AudioFirstFailureAtUtc);
        Assert.Null(message.AudioFailureCode);
        Assert.False(message.RegisterAudioFailure(Now.AddHours(1), "Files.Content.Unavailable"));
        Assert.Equal(1, message.AudioFailureCount);
        Assert.Equal(Now.AddHours(1), message.AudioFirstFailureAtUtc);
    }

    [Fact]
    public void A_conflicting_audio_copy_keeps_the_acquisition_failure_metadata()
    {
        var message = Message();
        Assert.True(message.SetAudioDescriptor("123", "audio/ogg", new string('a', 64)));
        Assert.True(message.StoreAudio("private/audio", 47).IsSuccess);
        Assert.False(message.RegisterAudioFailure(Now, "Files.Content.Unavailable"));

        Assert.True(message.StoreAudio("private/another-audio", 47).IsFailure);

        Assert.Equal(1, message.AudioFailureCount);
        Assert.Equal(Now, message.AudioFirstFailureAtUtc);
        Assert.Equal("Files.Content.Unavailable", message.AudioFailureCode);
    }
}
