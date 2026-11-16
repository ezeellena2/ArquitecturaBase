using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantHomeAudioTests
{
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0xF7, 0x81];
    private static readonly byte[] Mp4 = [0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D];

    [Fact]
    public void Browser_recordings_are_recognized_by_their_bytes()
    {
        Assert.Equal("audio/webm", AiAssistantAudioPolicy.Detect(Webm));
        Assert.Equal("audio/mp4", AiAssistantAudioPolicy.Detect(Mp4));
        Assert.Null(AiAssistantAudioPolicy.Detect("OggS fictional audio"u8));
        Assert.Null(AiAssistantAudioPolicy.Detect("not audio at all"u8));
        Assert.Null(AiAssistantAudioPolicy.Detect(Webm.AsSpan(0, 3)));
    }

    [Fact]
    public void A_transcript_is_read_only_from_the_closed_result_of_a_normalization()
    {
        var audio = AiAssistantAudioPolicy.ReadNormalized("""{"kind":"normalizedAudio","languages":["es"],"text":"Armame un formulario de visitas"}""", 4000);
        Assert.Equal("Armame un formulario de visitas", audio?.Text);
        Assert.Null(AiAssistantAudioPolicy.ReadNormalized("""{"kind":"answer","languages":["es"],"text":"Hola"}""", 4000));
        Assert.Null(AiAssistantAudioPolicy.ReadNormalized("""{"kind":"normalizedAudio","languages":["es"],"text":"Hola","extra":1}""", 4000));
        Assert.Null(AiAssistantAudioPolicy.ReadNormalized("""{"kind":"normalizedAudio","languages":["es"],"text":"Hola"}""", 3));
        Assert.Null(AiAssistantAudioPolicy.ReadNormalized("not json", 4000));
    }

    [Fact]
    public void A_dictated_turn_reads_the_typed_text_and_then_what_was_said()
    {
        Assert.Equal("Typed", AiAssistantAudioPolicy.Spoken("Typed", null));
        Assert.Equal("Said", AiAssistantAudioPolicy.Spoken("  ", "Said"));
        Assert.Equal("Typed\n\nSaid", AiAssistantAudioPolicy.Spoken("Typed", "Said"));
    }

    [Fact]
    public void The_private_key_names_its_request_and_only_those_keys_are_recognized()
    {
        var requestId = Guid.NewGuid();
        foreach (var type in new[] { "audio/webm", "audio/mp4", "audio/ogg", "audio/wav" })
        {
            Assert.True(AiAssistantHomeAudioPolicy.TryParseKey(AiAssistantHomeAudioPolicy.Key(requestId, type), out var parsed));
            Assert.Equal(requestId, parsed);
        }
        Assert.Equal($"ai/assistant/audio/{requestId:N}.webm", AiAssistantHomeAudioPolicy.Key(requestId, "audio/webm"));
        Assert.False(AiAssistantHomeAudioPolicy.TryParseKey($"whatsapp/audio/{requestId:N}.ogg", out _));
        Assert.False(AiAssistantHomeAudioPolicy.TryParseKey($"ai/assistant/audio/{requestId:N}.mp3", out _));
        Assert.False(AiAssistantHomeAudioPolicy.TryParseKey($"ai/assistant/audio/{requestId:N}.webm.thumb.jpg", out _));
        Assert.False(AiAssistantHomeAudioPolicy.TryParseKey($"ai/assistant/audio/{Guid.Empty:N}.webm", out _));
    }

    [Fact]
    public async Task Storing_keeps_one_copy_and_rejects_another_recording_under_the_same_request()
    {
        var files = new MemoryPrivateFileStore();
        var policy = new AiAssistantHomeAudioPolicy(files, null!, null!, new FakeTimeProvider());
        var requestId = Guid.NewGuid();
        var expiresAtUtc = new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc);
        var stored = await policy.StoreAsync(requestId, Webm, "audio/webm", expiresAtUtc, TestContext.Current.CancellationToken);
        Assert.True(stored.IsSuccess);
        Assert.Equal(AiAssistantHomeAudioPolicy.Key(requestId, "audio/webm"), stored.Value.StorageKey);
        Assert.Equal(Webm.Length, stored.Value.Size);
        Assert.Null(AiAssistantAudioPolicy.Validate(stored.Value));
        var again = await policy.StoreAsync(requestId, Webm, "audio/webm", expiresAtUtc, TestContext.Current.CancellationToken);
        Assert.Equal(stored.Value.Sha256, again.Value.Sha256);
        var other = await policy.StoreAsync(requestId, [.. Webm, 0x00], "audio/webm", expiresAtUtc, TestContext.Current.CancellationToken);
        Assert.Equal(AiAssistantErrors.RequestReused, other.Error);
        Assert.Equal(1, files.WriteCount);
    }

    [Fact]
    public async Task Retention_keeps_only_the_recordings_that_a_turn_still_names()
    {
        var kept = Guid.NewGuid();
        var gone = Guid.NewGuid();
        var reader = new RetainedReader([kept], [kept, gone]);
        var keys = new[] { AiAssistantHomeAudioPolicy.Key(kept, "audio/webm"), AiAssistantHomeAudioPolicy.Key(gone, "audio/mp4"),
            $"whatsapp/audio/{kept:N}.ogg", "forms/pdf/fictional.pdf" };
        var referenced = await new AiAssistantAudioRetentionSource(reader).ListReferencedKeysAsync(keys, TestContext.Current.CancellationToken);
        Assert.Equal([keys[0]], referenced);
        Assert.Equal(new[] { kept, gone }.Order(), reader.Asked.Order());
        Assert.Empty(await new AiAssistantAudioRetentionSource(reader).ListReferencedKeysAsync(["forms/pdf/fictional.pdf"], TestContext.Current.CancellationToken));
    }

    // I2, review: a recording nobody can listen to any more goes. Its normalization lost its content (its conversation was removed), so
    // the copy is released although another conversation's turn still names it; that turn keeps its sealed transcript.
    [Fact]
    public async Task Retention_releases_a_recording_whose_normalization_lost_its_content_even_if_a_turn_names_it()
    {
        var heard = Guid.NewGuid();
        var purged = Guid.NewGuid();
        var reader = new RetainedReader([heard, purged], [heard]);
        var keys = new[] { AiAssistantHomeAudioPolicy.Key(heard, "audio/webm"), AiAssistantHomeAudioPolicy.Key(purged, "audio/webm") };

        var referenced = await new AiAssistantAudioRetentionSource(reader).ListReferencedKeysAsync(keys, TestContext.Current.CancellationToken);

        Assert.Equal([keys[0]], referenced);
        Assert.Equal(AiAssistantChannelPolicy.HomeAudioChannelId, reader.Channel);
    }

    [Fact]
    public void The_microphone_needs_a_profile_for_whole_browser_recordings_and_private_storage()
    {
        var connection = new AiConnectionRow(AiProvider.OpenAI, "model", "encrypted-key", true, 1);
        var browser = new AiAssistantAudioModelProfile(AiProvider.OpenAI, "audio-model", "browser-audio", 1, ["audio/webm", "audio/mp4"], 1_000_000);
        Assert.True(Guard([browser], new MemoryPrivateFileStore()).HearsHomeAudio(connection));
        Assert.False(Guard([browser], new MemoryPrivateFileStore(available: false)).HearsHomeAudio(connection));
        Assert.False(Guard([browser with { ContentTypes = ["audio/ogg"] }], new MemoryPrivateFileStore()).HearsHomeAudio(connection));
        Assert.False(Guard([browser with { MaximumAudioBytes = 500_000 }], new MemoryPrivateFileStore()).HearsHomeAudio(connection));
        Assert.False(Guard([browser], new MemoryPrivateFileStore()).HearsHomeAudio(connection with { Enabled = false }));
        Assert.False(Guard([browser], new MemoryPrivateFileStore()).HearsHomeAudio(connection with { ProtectedApiKey = null }));
        Assert.False(Guard([browser], new MemoryPrivateFileStore()).HearsHomeAudio(connection with { Provider = AiProvider.Anthropic }));
    }

    [Fact]
    public async Task A_recording_that_is_not_audio_or_too_long_is_rejected_before_storing()
    {
        var host = new ServiceHost();
        byte[] tooLong = [.. Webm, .. new byte[SubmitAiAssistantAudioRequest.MaximumBytes]];
        foreach (var content in new[] { "fictional text"u8.ToArray(), tooLong, Array.Empty<byte>() })
        {
            var result = await host.Service.SubmitAsync(new(Guid.NewGuid(), "es", content), TestContext.Current.CancellationToken);
            Assert.Contains("file", Assert.IsType<ValidationError>(result.Error).Errors.Keys);
        }
        Assert.Equal(0, host.Files.WriteCount);
        Assert.Empty(host.Operations.Submitted);
    }

    [Fact]
    public async Task A_new_recording_is_stored_and_normalized_in_its_own_channel_without_capabilities()
    {
        var host = new ServiceHost();
        var requestId = Guid.NewGuid();
        var result = await host.Service.SubmitAsync(new(requestId, "es", Webm), TestContext.Current.CancellationToken);
        Assert.Equal(new AiAssistantAudioResponse(requestId, "pending", null, null, new AiAssistantLimits().PollingAfterSeconds), result.Value);
        var submitted = Assert.Single(host.Operations.Submitted);
        Assert.Equal(requestId, submitted.RequestId);
        Assert.Equal(AiAssistantChannelPolicy.HomeAudioChannelId, submitted.Context.ChannelId);
        Assert.Equal(host.ActorId, submitted.Context.ActorUserId);
        Assert.Null(submitted.Context.SubjectUserId);
        Assert.Empty(submitted.Context.AllowedCapabilities);
        Assert.Empty(submitted.Context.AllowedTerminals);
        Assert.Equal("", submitted.Text);
        Assert.Equal(AiAssistantHomeAudioPolicy.Key(requestId, "audio/webm"), submitted.Audio?.StorageKey);
        Assert.Equal("audio/webm", submitted.Audio?.ContentType);
        Assert.Equal(1, host.Files.WriteCount);
    }

    // I14: what the acceptance would refuse (the quota, the capacity, the transcription profile) refuses before the copy is kept,
    // with the same error and retryAfter, and nothing is submitted.
    [Theory]
    [InlineData("quota")]
    [InlineData("capacity")]
    [InlineData("profile")]
    public async Task A_recording_the_acceptance_would_refuse_is_not_kept(string refusal)
    {
        var host = new ServiceHost(transcribes: refusal != "profile");
        if (refusal == "quota") host.Fixture.SpendQuota(host.ActorId, AiAssistantChannelPolicy.HomeAudioChannelId, host.Now, new AiAssistantLimits().ActorDailyQuota, isAudio: true);
        if (refusal == "capacity") host.Fixture.FillCapacity(host.Now);
        var error = (await host.Service.SubmitAsync(new(Guid.NewGuid(), "es", Webm), TestContext.Current.CancellationToken)).Error;
        var expected = refusal switch
        {
            "quota" => AiAssistantErrors.QuotaExceeded((int)(host.Now.Date.AddDays(1) - host.Now).TotalSeconds),
            "capacity" => AiAssistantErrors.CapacityLimited(new AiAssistantLimits().PollingAfterSeconds),
            _ => AiAssistantErrors.Unavailable,
        };
        Assert.Equal((expected.Code, expected.Metadata?["retryAfter"]), (error.Code, error.Metadata?["retryAfter"]));
        Assert.Equal(0, host.Files.WriteCount);
        Assert.Empty(host.Operations.Submitted);
    }

    // I14, review: without private storage the recording says so before a spent quota, which would only say to wait until it renews.
    [Fact]
    public async Task Without_private_storage_a_recording_says_so_before_a_spent_quota()
    {
        var host = new ServiceHost(storage: false);
        host.Fixture.SpendQuota(host.ActorId, AiAssistantChannelPolicy.HomeAudioChannelId, host.Now, new AiAssistantLimits().ActorDailyQuota, isAudio: true);
        var error = (await host.Service.SubmitAsync(new(Guid.NewGuid(), "es", Webm), TestContext.Current.CancellationToken)).Error;
        Assert.Equal(FileErrors.Unavailable, error);
        Assert.Empty(host.Operations.Submitted);
    }

    // I14, review: a retry with the audio quota spent recovers its normalization: the lookup goes before the check.
    [Fact]
    public async Task A_retry_with_the_audio_quota_spent_recovers_its_normalization()
    {
        var host = new OperationsHost();
        host.Fixture.SpendQuota(host.ActorId, AiAssistantChannelPolicy.HomeAudioChannelId, host.Now, new AiAssistantLimits().ActorDailyQuota - 1,
            isAudio: true);
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(Webm, requestId)).IsSuccess);
        var again = await host.UploadAsync(Webm, requestId);
        Assert.True(again.IsSuccess, again.IsFailure ? again.Error.Code : null);
        Assert.Equal((requestId, "pending"), (again.Value.RequestId, again.Value.State));
        Assert.Equal((1, 1), (host.Files.WriteCount, host.Fixture.Store.Turns.Count));
    }

    [Fact]
    public async Task Repeating_a_recording_returns_its_normalization_without_storing_or_submitting_again()
    {
        var host = new ServiceHost();
        var requestId = Guid.NewGuid();
        host.Operations.Known[requestId] = new AssistantOperationResponse(requestId, "completed", null, null, null)
            { NormalizedAudio = new("Armame un formulario de visitas", ["es"]) };
        var result = await host.Service.SubmitAsync(new(requestId, "es", Webm), TestContext.Current.CancellationToken);
        Assert.Equal("completed", result.Value.State);
        Assert.Equal("Armame un formulario de visitas", result.Value.Transcript);
        Assert.Equal(0, host.Files.WriteCount);
        Assert.Empty(host.Operations.Submitted);
    }

    [Fact]
    public async Task A_failed_normalization_says_why_and_offers_a_new_attempt()
    {
        var host = new ServiceHost();
        var requestId = Guid.NewGuid();
        host.Operations.Known[requestId] = new AssistantOperationResponse(requestId, "failed", null, null, "Ai.Assistant.ProviderUnavailable");
        var result = await host.Service.GetAsync(requestId, "es", TestContext.Current.CancellationToken);
        Assert.Null(result.Value.Transcript);
        Assert.Equal("Ai.Assistant.ProviderUnavailable", result.Value.Failure?.Code);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Failure?.Detail));
        Assert.Equal("newAttempt", result.Value.Failure?.Recovery);
        Assert.Equal(AiAssistantErrors.NotFound, (await host.Service.GetAsync(Guid.NewGuid(), "es", TestContext.Current.CancellationToken)).Error);
    }

    [Fact]
    public async Task Without_the_assistant_permission_nothing_is_stored_or_read()
    {
        var host = new ServiceHost();
        host.Permissions.Permissions[host.ActorId] = [];
        var requestId = Guid.NewGuid();
        Assert.Equal(AiAssistantErrors.Forbidden, (await host.Service.SubmitAsync(new(requestId, "es", Webm), TestContext.Current.CancellationToken)).Error);
        Assert.Equal(AiAssistantErrors.Forbidden, (await host.Service.GetAsync(requestId, "es", TestContext.Current.CancellationToken)).Error);
        Assert.Equal(AiAssistantErrors.Forbidden, (await host.Service.OpenAsync(requestId, TestContext.Current.CancellationToken)).Error);
        Assert.Equal(0, host.Files.WriteCount);
        Assert.Empty(host.Operations.Submitted);
        Assert.Equal(0, host.Operations.Reads);
    }

    // I13: a retry is the same recording again (SHA-256, size and type, compared with the normalization's descriptor in any state); another
    // recording under the same request is a conflict, and nothing is kept or submitted again.
    [Theory]
    [InlineData("pending")]
    [InlineData("completed")]
    [InlineData("failed")]
    public async Task Repeating_the_request_with_another_recording_is_a_conflict_whatever_the_state_of_its_normalization(string state)
    {
        var host = new OperationsHost();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(Webm, requestId)).IsSuccess);
        if (state == "completed") host.Complete(requestId, "Armame un formulario de visitas");
        if (state == "failed")
        {
            var turn = host.Turn(requestId);
            Assert.True(turn.Claim(Guid.NewGuid(), host.Now.AddSeconds(90)).IsSuccess);
            Assert.True(turn.Fail(turn.Fence, AiAssistantErrors.InvalidOutput.Code, false, host.Now).IsSuccess);
        }

        Assert.Equal(AiAssistantErrors.RequestReused, (await host.UploadAsync([.. Webm, 0x01], requestId)).Error);
        Assert.Equal(AiAssistantErrors.RequestReused, (await host.UploadAsync(Mp4, requestId)).Error);
        var again = await host.UploadAsync(Webm, requestId);
        Assert.Equal(state, again.Value.State);
        Assert.Equal(state == "completed" ? "Armame un formulario de visitas" : null, again.Value.Transcript);
        Assert.Equal((1, 1, 1), (host.Files.WriteCount, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));
    }

    // I13: two uploads of the same recording at once: the second does not see the first's normalization yet, keeps the same copy, and its
    // acceptance meets the first's, sealed at another instant: it answers with that normalization, as a retry would. When the first took
    // the last audio of the day (a resend after a network cut), the second's check refuses and looks for it once more (I14, review).
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Two_overlapping_uploads_of_the_same_recording_end_in_one_normalization(bool lastPlace)
    {
        var host = new OperationsHost();
        var spent = lastPlace ? new AiAssistantLimits().ActorDailyQuota - 1 : 0;
        host.Fixture.SpendQuota(host.ActorId, AiAssistantChannelPolicy.HomeAudioChannelId, host.Now, spent, isAudio: true);
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(Webm, requestId)).IsSuccess);
        host.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Fixture.Store.HiddenAcceptedLookups = 1;

        var second = await host.UploadAsync(Webm, requestId);

        Assert.True(second.IsSuccess, second.IsFailure ? second.Error.Code : null);
        Assert.Equal((requestId, "pending"), (second.Value.RequestId, second.Value.State));
        Assert.Equal((1, 1, spent + 1), (host.Files.WriteCount, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));
    }

    // I13, review: when the acceptance meets the other upload's and the lookup after it fails for another reason (here the permission was
    // taken away in between), that reason answers, and not the conflict, which the browser would take for another recording.
    [Fact]
    public async Task An_overlapping_upload_whose_second_lookup_fails_answers_that_failure_and_not_a_conflict()
    {
        var host = new OperationsHost();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(Webm, requestId)).IsSuccess);
        host.Fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        host.Fixture.Store.HiddenAcceptedLookups = 1;
        var lookups = 0;
        host.Fixture.Store.BeforeLookup = () => { if (++lookups == 2) host.Fixture.Permissions.Permissions[host.ActorId] = []; };

        Assert.Equal(AiAssistantErrors.Forbidden, (await host.UploadAsync(Webm, requestId)).Error);
        Assert.Equal((2, 1, 1), (lookups, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));
    }

    // B2: a recording belongs to its person and its request: changing the language or the zone while it is heard does not lose it,
    // neither for its status nor for a retry, and another person with the same request never finds it.
    [Fact]
    public async Task Another_language_or_zone_finds_the_recording_only_for_its_person()
    {
        var host = new OperationsHost();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(Webm, requestId)).IsSuccess);

        var english = await host.Service.GetAsync(requestId, "en", TestContext.Current.CancellationToken);
        Assert.True(english.IsSuccess, english.IsFailure ? english.Error.Code : null);
        await host.Fixture.Users.UpdateProfileAsync(host.ActorId, null, "en", "Europe/Madrid", TestContext.Current.CancellationToken);
        host.Complete(requestId, "Armame un formulario de visitas");
        Assert.Equal("Armame un formulario de visitas", (await host.Service.GetAsync(requestId, "en", TestContext.Current.CancellationToken)).Value.Transcript);
        var retried = await host.UploadAsync(Webm, requestId, "en");
        Assert.Equal(("completed", "Armame un formulario de visitas"), (retried.Value.State, retried.Value.Transcript));
        Assert.Equal((1, 1, 1), (host.Files.WriteCount, host.Fixture.Store.Turns.Count, host.Fixture.Store.Receipts.Count));

        host.Current.UserId = host.OtherId;
        host.Fixture.Permissions.Permissions[host.OtherId] = [ArtificialIntelligencePermissions.AssistantUse];
        Assert.Equal(AiAssistantErrors.NotFound, (await host.Service.GetAsync(requestId, "es", TestContext.Current.CancellationToken)).Error);
    }

    // B2, review: another person uploading under the same request never finds nor reuses the first one's normalization. The copy's key
    // does not name the person, so another recording meets the copy kept under that request (409, as before B2) and the same one starts
    // a normalization of its own; neither says the first one's state nor what was said.
    [Fact]
    public async Task Another_person_uploading_under_the_same_request_never_gets_the_first_ones_normalization()
    {
        var host = new OperationsHost();
        var requestId = Guid.NewGuid();
        Assert.True((await host.UploadAsync(Webm, requestId)).IsSuccess);
        host.Complete(requestId, "Armame un formulario de visitas");
        host.Current.UserId = host.OtherId;
        host.Fixture.Permissions.Permissions[host.OtherId] = [ArtificialIntelligencePermissions.AssistantUse];

        Assert.Equal(AiAssistantErrors.RequestReused, (await host.UploadAsync([.. Webm, 0x01], requestId)).Error);
        var same = await host.UploadAsync(Webm, requestId, "en");

        Assert.Equal((requestId, "pending", null), (same.Value.RequestId, same.Value.State, same.Value.Transcript));
        Assert.Equal([host.ActorId, host.OtherId], host.Fixture.Store.Turns.Select(turn => turn.OwnerUserId));
        Assert.Equal((1, 2), (host.Files.WriteCount, host.Fixture.Store.Receipts.Count));
        host.Current.UserId = host.ActorId;
        Assert.Equal("Armame un formulario de visitas", (await host.Service.GetAsync(requestId, "es", TestContext.Current.CancellationToken)).Value.Transcript);
    }

    private static AiAssistantAccessGuard Guard(AiAssistantAudioModelProfile[] audioProfiles, MemoryPrivateFileStore files)
    {
        var permissions = new FakePermissionService();
        return new(new InMemoryUserAccounts(), permissions, [], new AiAssistantLimits(), new FakeCurrentUser(),
            new AiAssistantCapabilityCatalogPolicy([], permissions), new(audioProfiles, [], files), FictionalCredentials.Policy);
    }

    private sealed class ServiceHost
    {
        public InMemoryUserAccounts Users { get; } = new();
        public FakePermissionService Permissions { get; } = new();
        public MemoryPrivateFileStore Files { get; }
        public RecordingOperations Operations { get; } = new();
        // The acceptance's counts and the saved connection, which the check before keeping the copy reads (I14).
        public AssistantOperationsFixture Fixture { get; } = new();
        public Guid ActorId { get; }
        public DateTime Now { get; } = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
        public AiAssistantAudioService Service { get; }

        public ServiceHost(bool transcribes = true, bool storage = true)
        {
            Files = new(storage);
            ActorId = Users.AddUser("dictating@example.test").Id;
            Permissions.Permissions[ActorId] = [ArtificialIntelligencePermissions.AssistantUse];
            var clock = new FakeTimeProvider(new DateTimeOffset(Now));
            var limits = new AiAssistantLimits();
            var current = new FakeCurrentUser { UserId = ActorId };
            var catalog = new AiAssistantCapabilityCatalogPolicy([], Permissions);
            AiAssistantAudioModelProfile[] hears = transcribes ? [new(AiProvider.OpenAI, "audio-model", "fixture-audio", 1, ["audio/webm", "audio/mp4"], 25000000)] : [];
            var guard = new AiAssistantAccessGuard(Users, Permissions, [], limits, current, catalog, new(hears, [], Files), FictionalCredentials.Policy);
            var responses = new AiAssistantResponsePolicy(Fixture.Protector, clock, catalog, NullLogger<AiAssistantResponsePolicy>.Instance);
            var submissions = new AiAssistantSubmissionPolicy(Fixture.Store, Fixture.Store, Fixture.Store, guard, Fixture.Protector,
                new OperationsFingerprint(), limits, responses);
            Service = new(Operations, new AiAssistantHomeAudioPolicy(Files, null!, null!, clock), guard, submissions, current, limits, clock,
                NullLogger<AiAssistantAudioService>.Instance);
        }
    }

    // The service over the real operations and acceptance, with a transcription profile for browser recordings: a retry and an overlap
    // meet the receipt the first upload left (I13).
    private sealed class OperationsHost
    {
        public AssistantOperationsFixture Fixture { get; } = new();
        public MemoryPrivateFileStore Files { get; } = new();
        public FakeCurrentUser Current { get; }
        public AiAssistantAudioService Service { get; }
        public Guid ActorId => Fixture.ActorId;
        public Guid OtherId => Fixture.SubjectId;
        public DateTime Now => Fixture.Clock.GetUtcNow().UtcDateTime;

        public OperationsHost()
        {
            Fixture.Permissions.Permissions[ActorId] = [ArtificialIntelligencePermissions.AssistantUse];
            Current = new FakeCurrentUser { UserId = ActorId };
            var limits = new AiAssistantLimits();
            var catalog = new AiAssistantCapabilityCatalogPolicy([], Fixture.Permissions);
            AiAssistantAudioModelProfile[] hears = [new(AiProvider.OpenAI, "audio-model", "fixture-audio", 1, ["audio/webm", "audio/mp4"], 25000000)];
            var guard = new AiAssistantAccessGuard(Fixture.Users, Fixture.Permissions, Fixture.Profiles, limits, Current, catalog,
                new(hears, [], Files), FictionalCredentials.Policy);
            var responses = new AiAssistantResponsePolicy(Fixture.Protector, Fixture.Clock, catalog, NullLogger<AiAssistantResponsePolicy>.Instance);
            var submissions = new AiAssistantSubmissionPolicy(Fixture.Store, Fixture.Store, Fixture.Store, guard, Fixture.Protector,
                new OperationsFingerprint(), limits, responses);
            var operations = new AiAssistantOperations(Fixture.Store, guard, submissions, Fixture.Work, Fixture.Store, responses,
                NullLogger<AiAssistantOperations>.Instance);
            Service = new(operations, new AiAssistantHomeAudioPolicy(Files, Fixture.Store, Fixture.Protector, Fixture.Clock), guard, submissions,
                Current, limits, Fixture.Clock, NullLogger<AiAssistantAudioService>.Instance);
        }

        public Task<Result<AiAssistantAudioResponse>> UploadAsync(byte[] content, Guid requestId, string culture = "es") =>
            Service.SubmitAsync(new(requestId, culture, content), TestContext.Current.CancellationToken);

        public AiAssistantTurn Turn(Guid requestId) => Fixture.Store.Turns.Single(turn => turn.RequestId == requestId);

        // The normalization as the worker would leave it, with what was said.
        public void Complete(Guid requestId, string text)
        {
            var turn = Turn(requestId);
            Assert.True(turn.Claim(Guid.NewGuid(), Now.AddSeconds(90)).IsSuccess);
            var answer = $$"""{"kind":"normalizedAudio","languages":["es"],"text":"{{text}}"}""";
            Assert.True(turn.Complete(turn.Fence, Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "answer", turn.PayloadRevision,
                answer), Fixture.Protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "evidences", turn.PayloadRevision, "[]"), Now).IsSuccess);
        }
    }

    private sealed class RecordingOperations : IAssistantOperations
    {
        public List<AssistantOperationRequest> Submitted { get; } = [];
        public Dictionary<Guid, AssistantOperationResponse> Known { get; } = [];
        public int Reads { get; private set; }
        public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken cancellationToken)
        {
            Submitted.Add(request);
            return Task.FromResult<Result<AssistantOperationAcceptedResponse>>(new AssistantOperationAcceptedResponse(request.RequestId, "pending"));
        }
        public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult<Result<AssistantOperationResponse>>(context.ChannelId == AiAssistantChannelPolicy.HomeAudioChannelId
                && Known.TryGetValue(requestId, out var known) ? known : AiAssistantErrors.NotFound);
        }
    }

    // Named: the recordings a turn with content names; heard: the normalizations that keep their content.
    private sealed class RetainedReader(IReadOnlyCollection<Guid> named, IReadOnlyCollection<Guid> heard) : IAiAssistantReader
    {
        public List<Guid> Asked { get; } = [];
        public string? Channel { get; private set; }
        public Task<IReadOnlyList<Guid>> ListRetainedAudioRequestIdsAsync(IReadOnlyCollection<Guid> audioRequestIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(audioRequestIds.Where(named.Contains).ToArray());
        public Task<IReadOnlyList<string>> ListRetainedAttachmentKeysAsync(IReadOnlyCollection<string> attachmentKeys, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListRetainedRequestIdsAsync(string channelId, IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken)
        {
            Channel = channelId;
            Asked.AddRange(requestIds);
            return Task.FromResult<IReadOnlyList<Guid>>(requestIds.Where(heard.Contains).ToArray());
        }
        public Task<IReadOnlyDictionary<Guid, string>> ListProtectedAttachmentsAsync(Guid ownerUserId, IReadOnlyCollection<Guid> turnIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountPendingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
