using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AssistantAudioExecutionFixture
{
    public AssistantOperationsFixture Inner { get; } = new(qualified: false, audioQualified: true);
    public AiAssistantLimits Limits { get; } = new();
    public AiAssistantCapabilityCatalogPolicy Catalog { get; }
    public AiAssistantAccessGuard Guard { get; }
    public AiAssistantExecutionProfileSnapshot Profile { get; }
    public AiAssistantTurn Turn { get; }
    public AssistantAudioInput Audio { get; }
    public AiAssistantClaimResponse Claim { get; }
    public AiAssistantContextPolicy ContextPolicy { get; }
    public AiAssistantExecutionService Execution { get; }
    public AiAssistantDispatchService Dispatch { get; }
    public AudioConversationClient Client { get; } = new();
    public AssistantAudioExecutionFixture()
    {
        Inner.Permissions.Permissions[Inner.ActorId] = ["fictional.channel.read"];
        var now = Inner.Clock.GetUtcNow().UtcDateTime;
        var channel = Inner.Context with { SubjectUserId = null, RequiredPermissions = new(["fictional.channel.read"], []), AllowedCapabilities = [], AllowedTerminals = [] };
        Profile = new("fixture-audio", 2, "audio-none-v1", 1, channel) { Purpose = AiAssistantPurpose.NormalizeAudio, OccurredAtUtc = now.AddMinutes(-1) };
        var conversation = AiConversation.Create(Inner.ActorId, now, channel.ChannelId);
        Turn = AiAssistantTurn.Create(Inner.ActorId, conversation.Id, Guid.NewGuid(), 1, "", "en", "UTC", AiProvider.OpenAI,
            "audio-model", 1, now, now.AddHours(1));
        Audio = new("audio/private-object", "audio.ogg", "audio/ogg; codecs=opus", 42, new string('a', 64), Turn.ContentExpiresAtUtc);
        Turn.SealExecutionProfile(JsonSerializer.Serialize(Profile, AiAssistantResponsePolicy.JsonOptions));
        ProtectDescriptor(Audio);
        Turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        Inner.Store.Conversations.Add(conversation);
        Inner.Store.Turns.Add(Turn);
        Claim = new(Turn.Id, Inner.ActorId, conversation.Id, Turn.RequestId, Turn.Fence, Turn.AttemptId!.Value, Turn.DeadlineAtUtc!.Value, "en");
        Catalog = new([Inner.Source], Inner.Permissions);
        Guard = new(Inner.Users, Inner.Permissions, [], Limits, new FakeCurrentUser(), Catalog, Inner.AudioProfiles);
        var responses = new AiAssistantResponsePolicy(Inner.Protector, Inner.Clock, Catalog);
        ContextPolicy = new(Inner.Store, Inner.Store, Guard, responses, new AiConnectionCandidatePolicy(new AudioSecret()), Catalog, Limits, Inner.Clock);
        Execution = new(ContextPolicy, Guard, Catalog, null!, Client, Limits, Inner.Clock, NullLogger<AiAssistantExecutionService>.Instance);
        Dispatch = new(Inner.Store, Inner.Work, new(Inner.Store, Inner.Store, Inner.Store, Guard, Catalog), Inner.Protector, Limits, Inner.Clock,
            NullLogger<AiAssistantDispatchService>.Instance);
    }
    public void ProtectDescriptor(AssistantAudioInput descriptor) => Turn.ProtectQuestion(Inner.Protector.Protect(Inner.ActorId, Turn.ConversationId,
        Turn.Id, "question", Turn.PayloadRevision, JsonSerializer.Serialize(descriptor, AiAssistantResponsePolicy.JsonOptions)));
    public async Task<AiAssistantExecutionContextResponse> ContextAsync(CancellationToken ct)
    {
        var row = (await Inner.Store.FindTurnAsync(Turn.Id, ct))!;
        return new(Claim, row, new(AiProvider.OpenAI, "audio-model", "fixture-audio-key", "", [], [], [], null, 0, 262144, 131072)
        { Audio = Audio, AudioMaximumBytes = 25000000 }, [], [], 0, 0) { ExecutionProfile = Profile };
    }
    private sealed class AudioSecret : IAiSecretProtector
    {
        public string Protect(AiProvider provider, string key) => throw new NotSupportedException();
        public string? Unprotect(AiProvider provider, string key) => "fixture-audio-key";
    }
}
internal sealed class AudioConversationClient : ILlmConversationClient
{
    public int Calls { get; private set; }
    public Task<Result<LlmConversationResponse>> SendAsync(LlmConversationRequest request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("normalizedAudio", [], null, new(null, null) { AudioSeconds = 2.75m })
        { NormalizedAudio = new("Fictitious visit", ["en"]) });
    }
}
