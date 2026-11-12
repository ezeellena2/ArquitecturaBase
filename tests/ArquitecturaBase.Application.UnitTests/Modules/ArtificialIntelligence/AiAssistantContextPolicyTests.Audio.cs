using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// IA4: a dictated Home turn reads its typed text and then its transcript, in the context, the thread and the title.
public sealed partial class AiAssistantContextPolicyTests
{
    [Fact]
    public async Task A_dictated_Home_turn_sends_the_typed_text_and_then_what_was_said()
    {
        var host = new Host("es");
        host.Reader.Row = host.Reader.Row with { AudioRequestId = Guid.NewGuid(), ProtectedTranscript = "transcript-content" };
        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal("Current question\n\nDictated request", Assert.Single(result.Value.ProviderRequest.Messages).Text);
    }

    [Fact]
    public void The_thread_shows_the_transcript_beside_the_typed_text()
    {
        var host = new Host("es");
        var audioRequestId = Guid.NewGuid();
        var policy = new AiAssistantResponsePolicy(host.Protector, new FakeTimeProvider(), new AiAssistantCapabilityCatalogPolicy([], host.Permissions));
        var mapped = policy.Map(host.Reader.Row with { AudioRequestId = audioRequestId, ProtectedTranscript = "transcript-content" }, true, 2);
        Assert.Equal("Current question", mapped.Value.Question);
        Assert.Equal(new AiAssistantTurnAudioResponse(audioRequestId, "Dictated request"), mapped.Value.Audio);
        Assert.Null(policy.Map(host.Reader.Row, true, 2).Value.Audio);
    }

    [Fact]
    public void A_dictated_first_turn_without_typed_text_names_the_conversation_after_what_was_said()
    {
        var host = new Host("es");
        var row = host.Reader.Row;
        var policy = new AiAssistantResponsePolicy(host.Protector, new FakeTimeProvider(), new AiAssistantCapabilityCatalogPolicy([], host.Permissions));
        var conversation = new AiConversationRow(row.ConversationId, row.OwnerUserId, AiConversationState.Active, 1, row.SubmittedAtUtc, [], null,
            new AiConversationQuestionRow(row.Id, row.PayloadRevision, "blank-content", row.ContentExpiresAtUtc) { ProtectedTranscript = "transcript-content" });
        Assert.Equal("Dictated request", policy.MapTitle(conversation, true).Value);
        Assert.Equal("Current question", policy.MapTitle(conversation with { FirstQuestion = conversation.FirstQuestion! with { ProtectedQuestion = "current-content" } }, true).Value);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public async Task Bootstrap_offers_the_microphone_only_with_a_profile_for_browser_recordings_and_private_storage(bool profile, bool storage, bool audio)
    {
        var host = new Host("es");
        var catalog = new AiAssistantCapabilityCatalogPolicy([new BootstrapCatalogSource(HomeQueryIds)], host.Permissions);
        var limits = new AiAssistantLimits();
        AiAssistantAudioModelProfile[] audioProfiles = profile ? [new(AiProvider.OpenAI, "audio-model", "browser-audio", 1, ["audio/webm", "audio/mp4"], 1_000_000)] : [];
        var guard = new AiAssistantAccessGuard(host.Users, host.Permissions,
            [new(AiProvider.OpenAI, "model", limits.ContextBytes, true, true) { Id = "home" }], limits,
            new FakeCurrentUser { UserId = host.UserId }, catalog, new(audioProfiles, [], new MemoryPrivateFileStore(storage)));
        var reads = Bootstrap(host, guard, catalog, limits);
        var result = await reads.BootstrapAsync(TestContext.Current.CancellationToken);
        Assert.Equal(audio, result.Value.Audio);
    }
}
