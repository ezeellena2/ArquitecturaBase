using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I8, review: only what cannot be decrypted becomes «unavailable», and a page of unreadable rows leaves one warning, not one per row.
public sealed class AiAssistantResponsePolicyUnreadableTests
{
    private readonly FakeTimeProvider clock = new();
    private readonly FakeLogger<AiAssistantResponsePolicy> log = new();
    private readonly AiAssistantResponsePolicy policy;

    public AiAssistantResponsePolicyUnreadableTests() =>
        policy = new(new Protector(), clock, new AiAssistantCapabilityCatalogPolicy([], new FakePermissionService()), log);

    [Fact]
    public void A_page_of_unreadable_turns_leaves_one_warning_with_their_count_and_the_first_id()
    {
        var first = Turn("unreadable");
        var rows = new[] { first, Turn("readable"), Turn("unreadable") };

        var mapped = policy.MapReadable(rows, true, 2);

        Assert.True(mapped.IsSuccess);
        Assert.Equal(["unavailable", "allowed", "unavailable"], mapped.Value.Select(turn => turn.ContentAccess));
        var warning = Assert.Single(log.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("2 ", warning.Message, StringComparison.Ordinal);
        Assert.Contains(first.Id.ToString(), warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_of_unreadable_titles_leaves_one_warning_with_their_count_and_the_first_id()
    {
        var first = Conversation("unreadable");
        var rows = new[] { first, Conversation("readable"), Conversation("unreadable") };

        var summaries = policy.Summaries(rows, _ => true);

        Assert.Equal(["unavailable", "allowed", "unavailable"], summaries.Select(summary => summary.Access));
        var warning = Assert.Single(log.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Contains("2 ", warning.Message, StringComparison.Ordinal);
        Assert.Contains(first.Id.ToString(), warning.Message, StringComparison.Ordinal);
    }

    // A channel turn whose proposal belongs to a capability this copy no longer knows decrypts fine: it fails as before I8 instead of
    // passing for unreadable, and the log does not send anyone to check the key ring.
    [Fact]
    public void A_proposal_of_an_unknown_capability_fails_instead_of_becoming_unavailable()
    {
        var row = Turn("readable") with { ProtectedAnswer = "proposal-answer", ToolResults = [new(1, "fictional.forgotten", 1, "readable")] };

        var mapped = policy.MapReadable([row], true, 2);

        Assert.Equal(AiAssistantErrors.ContentUnavailable, mapped.Error);
        Assert.Empty(log.Collector.GetSnapshot());
    }

    private AiAssistantTurnRow Turn(string protectedQuestion)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, AiAssistantTurnState.Completed, "completed", 1, 1, [],
            protectedQuestion, null, null, 1, "es", "UTC", AiProvider.OpenAI, "model", 1, null, 0, now, now.AddDays(1), null, null, 0, 0, 0,
            null, null, false, null);
    }

    private AiConversationRow Conversation(string protectedQuestion) => new(Guid.NewGuid(), Guid.NewGuid(), AiConversationState.Active, 1,
        clock.GetUtcNow().UtcDateTime, [], null, new(Guid.NewGuid(), 1, protectedQuestion, clock.GetUtcNow().UtcDateTime.AddDays(1)));

    private sealed class Protector : IAiConversationProtector
    {
        public string Protect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string plaintext) =>
            throw new NotSupportedException();
        public string? Unprotect(Guid ownerUserId, Guid conversationId, Guid turnId, string purpose, long payloadRevision, string ciphertext) =>
            ciphertext switch
            {
                "unreadable" => null,
                "proposal-answer" => "{\"kind\":\"proposal\",\"ordinal\":1}",
                _ => "Fictional question",
            };
    }
}
