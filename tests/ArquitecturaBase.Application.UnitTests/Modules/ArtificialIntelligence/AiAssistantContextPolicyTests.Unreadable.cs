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
}
