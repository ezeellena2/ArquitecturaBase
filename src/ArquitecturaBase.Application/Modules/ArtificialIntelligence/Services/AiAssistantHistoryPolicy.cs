using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantHistoryPolicy
{
    // Oldest first within the character budget. Only the latest proposal travels with its content, so a requested
    // change can be proposed again in full; older ones were replaced and keep only their explanation.
    public static IReadOnlyList<LlmConversationMessage> Messages(IReadOnlyList<AiAssistantTurnResponse> newestFirst, int historyCharacters, string culture)
    {
        var latestProposal = newestFirst.FirstOrDefault(turn => turn.ObservedProposal is not null);
        var messages = new List<LlmConversationMessage>();
        var used = 0;
        foreach (var turn in newestFirst.Reverse())
        {
            if (turn.Question is null || turn.Answer is null) continue;
            var answer = turn.Answer.Text;
            if (ReferenceEquals(turn, latestProposal))
                answer += "\n\n" + string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.PreviousProposal", culture),
                    JsonSerializer.Serialize(turn.ObservedProposal!.Payload, turn.ObservedProposal.Payload.GetType(), AiAssistantResponsePolicy.JsonOptions));
            var length = turn.Question.Length + answer.Length;
            if (used + length > historyCharacters) continue;
            used += length;
            messages.Add(new("user", turn.Question));
            messages.Add(new("assistant", answer));
        }
        return messages;
    }
}
