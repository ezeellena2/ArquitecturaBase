using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I8: an earlier turn that cannot be decrypted is skipped as if it had expired: the turn still goes to the provider, without that turn
// and without the older history, and only its id is logged.
public sealed partial class AiAssistantContextPolicyTests
{
    [Fact]
    public async Task An_unreadable_earlier_turn_is_skipped_like_an_expired_one_and_only_its_id_is_logged()
    {
        var host = new Host("es");
        host.Protector.Contents["newer-question"] = "Newer question";
        host.Protector.Contents["newer-answer"] = JsonSerializer.Serialize(new AiAssistantAnswer("answer", "Newer answer", []), JsonSerializerOptions.Web);
        host.Protector.Unreadable.Add("unreadable-question");
        var current = host.Reader.Row with { Ordinal = 4 };
        host.Reader.Row = current;
        var unreadable = current with { Id = Guid.NewGuid(), Ordinal = 2, State = AiAssistantTurnState.Completed,
            ProtectedQuestion = "unreadable-question", ProtectedAnswer = "unreadable-answer" };
        host.Reader.History = [
            current,
            current with { Id = Guid.NewGuid(), Ordinal = 3, State = AiAssistantTurnState.Completed, ProtectedQuestion = "newer-question", ProtectedAnswer = "newer-answer" },
            unreadable,
            current with { Id = Guid.NewGuid(), Ordinal = 1, State = AiAssistantTurnState.Completed, ProtectedQuestion = "older-content" },
        ];

        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(["Newer question", "Newer answer", "Current question" + host.HomeTime], result.Value.ProviderRequest.Messages.Select(message => message.Text));
        // The current question, the newer turn's question and answer, and the unreadable question: the older turn is never decrypted.
        Assert.Equal(4, host.Protector.Decryptions);
        var log = Assert.Single(host.Log.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.Contains(unreadable.Id.ToString(), log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("unreadable-question", log.Message, StringComparison.Ordinal);
    }

    // I8, review: only what cannot be decrypted is skipped. An earlier proposal of a capability this copy no longer knows fails the turn
    // as before, so «Cambiar algo» never reaches the provider without the proposal it adjusts.
    [Fact]
    public async Task An_earlier_proposal_of_an_unknown_capability_fails_the_turn_instead_of_being_skipped()
    {
        var host = new Host("es");
        host.Protector.Contents["proposal-question"] = "Proposal question";
        host.Protector.Contents["proposal-answer"] = "{\"kind\":\"proposal\",\"ordinal\":1}";
        var current = host.Reader.Row;
        host.Reader.History = [
            current,
            current with { Id = Guid.NewGuid(), Ordinal = 2, State = AiAssistantTurnState.Completed, ProtectedQuestion = "proposal-question",
                ProtectedAnswer = "proposal-answer", ToolResults = [new(1, "fictional.forgotten", 1, "forgotten-result")] },
        ];

        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);

        Assert.Equal(AiAssistantErrors.ContentUnavailable, result.Error);
        Assert.Empty(host.Log.Collector.GetSnapshot());
    }

    // I7, review: a key that became unreadable after the turn was accepted closes it with Ai.Assistant.Unavailable, whose text the person
    // can act on; the one that names «Reemplazar clave» stays for the models catalog and the connection test.
    [Fact]
    public async Task A_saved_key_that_became_unreadable_fails_the_turn_as_unavailable()
    {
        var host = new Host("es");
        host.Secrets.Readable = false;

        var result = await host.Policy.PrepareAsync(host.Claim, TestContext.Current.CancellationToken);

        Assert.Equal(AiAssistantErrors.Unavailable, result.Error);
    }
}
