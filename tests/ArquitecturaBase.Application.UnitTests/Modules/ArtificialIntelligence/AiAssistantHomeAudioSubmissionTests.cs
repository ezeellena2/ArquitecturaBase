using System.Text.Json;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// IA4: the text of a dictated Home turn comes from its own completed normalization, never from the browser.
public sealed class AiAssistantHomeAudioSubmissionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("")]
    [InlineData("Armame un formulario")]
    public async Task A_dictated_turn_keeps_the_typed_text_and_seals_the_transcript_of_its_normalization(string typed)
    {
        var host = new Host();
        var audioRequestId = host.SeedNormalization(host.ActorId, "de visitas a clientes");
        var result = await host.SubmitAsync(audioRequestId, typed);
        Assert.True(result.IsSuccess);
        var turn = host.Fixture.Store.Turns.Single(item => item.RequestId == result.Value.RequestId);
        Assert.Equal(("home", audioRequestId), (turn.ChannelId, turn.AudioRequestId));
        Assert.Equal(typed, host.Reveal(turn, "question", turn.ProtectedQuestion!));
        Assert.Equal("de visitas a clientes", host.Reveal(turn, "transcript", turn.ProtectedTranscript!));
        Assert.Equal(1, host.Fixture.Work.Commits);
    }

    [Fact]
    public async Task A_dictated_turn_needs_a_completed_Home_normalization_of_the_same_person_with_its_content()
    {
        var host = new Host();
        var foreign = host.SeedNormalization(host.Fixture.Users.AddUser("other-dictating@example.test").Id, "Ajeno");
        var otherChannel = host.SeedNormalization(host.ActorId, "De otro canal", channelId: "fixture-operations");
        var running = host.SeedNormalization(host.ActorId, "Todavía no", complete: false);
        var expiring = host.SeedNormalization(host.ActorId, "Vencido", contentDays: 1);
        host.Fixture.Clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(AiAssistantErrors.NotFound, (await host.SubmitAsync(Guid.NewGuid(), "")).Error);
        Assert.Equal(AiAssistantErrors.NotFound, (await host.SubmitAsync(foreign, "")).Error);
        Assert.Equal(AiAssistantErrors.NotFound, (await host.SubmitAsync(otherChannel, "")).Error);
        Assert.Equal(AiAssistantErrors.ContentUnavailable, (await host.SubmitAsync(running, "")).Error);
        Assert.Equal(AiAssistantErrors.Expired, (await host.SubmitAsync(expiring, "")).Error);
        Assert.DoesNotContain(host.Fixture.Store.Turns, turn => turn.ChannelId == "home");
        Assert.Equal(0, host.Fixture.Work.Commits);
    }

    private sealed class Host
    {
        private readonly AiAssistantSubmissionPolicy submissions;
        public AssistantOperationsFixture Fixture { get; } = new(audioQualified: true);
        public Guid ActorId => Fixture.ActorId;

        public Host()
        {
            Fixture.Permissions.Permissions[ActorId] = [ArtificialIntelligencePermissions.AssistantUse];
            var limits = new AiAssistantLimits();
            var catalog = new AiAssistantCapabilityCatalogPolicy([new HomeQuerySource()], Fixture.Permissions);
            var guard = new AiAssistantAccessGuard(Fixture.Users, Fixture.Permissions, Fixture.Profiles, limits,
                new FakeCurrentUser { UserId = ActorId }, catalog, Fixture.AudioProfiles, new MemoryPrivateFileStore());
            submissions = new(Fixture.Store, Fixture.Store, Fixture.Store, guard, Fixture.Protector, new OperationsFingerprint(), limits, Fixture.Clock);
        }

        public Task<Result<AiAssistantTurnAcceptedResponse>> SubmitAsync(Guid audioRequestId, string question) =>
            Fixture.Work.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(ActorId,
                new(Guid.NewGuid(), null, null, Fixture.Store.Connection.Revision, question, "es") { AudioRequestId = audioRequestId }, ct),
                CommitPolicy.OnSuccess, Ct);

        public string? Reveal(AiAssistantTurn turn, string purpose, string ciphertext) =>
            Fixture.Protector.Unprotect(turn.OwnerUserId, turn.ConversationId, turn.Id, purpose, turn.PayloadRevision, ciphertext);

        public Guid SeedNormalization(Guid owner, string text, bool complete = true, int contentDays = 30,
            string channelId = AiAssistantChannelPolicy.HomeAudioChannelId)
        {
            var now = Fixture.Clock.GetUtcNow().UtcDateTime;
            var conversation = AiConversation.Create(owner, now, channelId);
            var requestId = Guid.NewGuid();
            var turn = AiAssistantTurn.Create(owner, conversation.Id, requestId, 1, "", "es", "UTC", AiProvider.OpenAI, "audio-model", 1,
                now, now.AddDays(contentDays), channelId);
            var profile = new AiAssistantExecutionProfileSnapshot("fixture-audio", 2, "audio-none-v1", 1,
                AiAssistantChannelPolicy.HomeAudio(owner, "es", "UTC") with { ChannelId = channelId }) { Purpose = AiAssistantPurpose.NormalizeAudio };
            Assert.True(turn.SealExecutionProfile(JsonSerializer.Serialize(profile, AiAssistantResponsePolicy.JsonOptions)).IsSuccess);
            turn.ProtectQuestion(Fixture.Protector.Protect(owner, conversation.Id, turn.Id, "question", turn.PayloadRevision, "{}"));
            Assert.True(turn.Claim(Guid.NewGuid(), now.AddSeconds(90)).IsSuccess);
            if (complete)
            {
                var answer = JsonSerializer.Serialize(new { kind = "normalizedAudio", text, languages = new[] { "es" } }, AiAssistantResponsePolicy.JsonOptions);
                Assert.True(turn.Complete(turn.Fence, Fixture.Protector.Protect(owner, conversation.Id, turn.Id, "answer", turn.PayloadRevision, answer),
                    Fixture.Protector.Protect(owner, conversation.Id, turn.Id, "evidences", turn.PayloadRevision, "[]"), now).IsSuccess);
            }
            Fixture.Store.Conversations.Add(conversation);
            Fixture.Store.Turns.Add(turn);
            Fixture.Store.Receipts.Add(AiAssistantRequestReceipt.Create(owner, requestId, "fixture-fingerprint", "test", conversation.Id, turn.Id,
                now, now.AddDays(30), channelId, true));
            return requestId;
        }
    }

    // The five core Home queries, as metadata only: accepting a turn never runs or parses a capability.
    private sealed class HomeQuerySource : IAssistantCapabilitySource
    {
        public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
            new[] { "users.summary", "roles.list", "roles.permissions", "system.help", "business.information" }.Select(id =>
                new AssistantCapabilityDefinition(id, 1, AssistantCapabilityKind.Read, "fixture",
                    """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
                    SystemQueryCapabilityCodec.OutputSchemaJson, new([], []), new(4096, 16384, 50, 5))).ToArray();
        public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Accepting a turn never runs a capability.");
        public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
            throw new InvalidOperationException("Accepting a turn never parses an output.");
    }
}
