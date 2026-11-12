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
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

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
        var reader = new RetainedReader([kept]);
        var keys = new[] { AiAssistantHomeAudioPolicy.Key(kept, "audio/webm"), AiAssistantHomeAudioPolicy.Key(gone, "audio/mp4"),
            $"whatsapp/audio/{kept:N}.ogg", "forms/pdf/fictional.pdf" };
        var referenced = await new AiAssistantAudioRetentionSource(reader).ListReferencedKeysAsync(keys, TestContext.Current.CancellationToken);
        Assert.Equal([keys[0]], referenced);
        Assert.Equal(new[] { kept, gone }.Order(), reader.Asked.Order());
        Assert.Empty(await new AiAssistantAudioRetentionSource(reader).ListReferencedKeysAsync(["forms/pdf/fictional.pdf"], TestContext.Current.CancellationToken));
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

    private static AiAssistantAccessGuard Guard(AiAssistantAudioModelProfile[] audioProfiles, MemoryPrivateFileStore files)
    {
        var permissions = new FakePermissionService();
        return new(new InMemoryUserAccounts(), permissions, [], new AiAssistantLimits(), new FakeCurrentUser(),
            new AiAssistantCapabilityCatalogPolicy([], permissions), new(audioProfiles, [], files));
    }

    private sealed class ServiceHost
    {
        public InMemoryUserAccounts Users { get; } = new();
        public FakePermissionService Permissions { get; } = new();
        public MemoryPrivateFileStore Files { get; } = new();
        public RecordingOperations Operations { get; } = new();
        public Guid ActorId { get; }
        public AiAssistantAudioService Service { get; }

        public ServiceHost()
        {
            ActorId = Users.AddUser("dictating@example.test").Id;
            Permissions.Permissions[ActorId] = [ArtificialIntelligencePermissions.AssistantUse];
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
            var limits = new AiAssistantLimits();
            var current = new FakeCurrentUser { UserId = ActorId };
            var guard = new AiAssistantAccessGuard(Users, Permissions, [], limits, current,
                new AiAssistantCapabilityCatalogPolicy([], Permissions), new([], [], Files));
            Service = new(Operations, new AiAssistantHomeAudioPolicy(Files, null!, null!, clock), guard, current, limits, clock,
                NullLogger<AiAssistantAudioService>.Instance);
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

    private sealed class RetainedReader(IReadOnlyCollection<Guid> retained) : IAiAssistantReader
    {
        public List<Guid> Asked { get; } = [];
        public Task<IReadOnlyList<Guid>> ListRetainedAudioRequestIdsAsync(IReadOnlyCollection<Guid> audioRequestIds, CancellationToken cancellationToken)
        {
            Asked.AddRange(audioRequestIds);
            return Task.FromResult<IReadOnlyList<Guid>>(audioRequestIds.Where(retained.Contains).ToArray());
        }
        public Task<IReadOnlyList<string>> ListRetainedAttachmentKeysAsync(IReadOnlyCollection<string> attachmentKeys, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> ListRetainedRequestIdsAsync(string channelId, IReadOnlyCollection<Guid> requestIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> ListProtectedAttachmentsAsync(Guid ownerUserId, IReadOnlyCollection<Guid> turnIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
