using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantHistoryPolicy
{
    // Oldest first within the character budget. Only the latest proposal travels with its content, so a requested
    // change can be proposed again in full; older ones were replaced and keep only their explanation. A channel keeps
    // no explanation, so its latest proposal travels alone and the older ones are left out.
    public static IReadOnlyList<LlmConversationMessage> Messages(IReadOnlyList<AiAssistantTurnResponse> newestFirst, int historyCharacters, string culture)
    {
        var latestProposal = newestFirst.FirstOrDefault(turn => turn.ObservedProposal is not null);
        var messages = new List<LlmConversationMessage>();
        var used = 0;
        foreach (var turn in newestFirst.Reverse())
        {
            var latest = ReferenceEquals(turn, latestProposal);
            if (turn.Question is null || turn.Answer is null && !latest) continue;
            var asked = AiAssistantAudioPolicy.Spoken(turn.Question, turn.Audio?.Transcript);
            // A turn that brought only a file is never an empty message, which a provider may reject; the file itself travels in the
            // current message, so only its name goes here, as a JSON string: the person chose it, so it never goes raw in their message.
            if (asked.Trim().Length == 0 && turn.AttachedDocument is { } document)
                asked = string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.AttachedDocument", culture), Quoted(document));
            else if (asked.Trim().Length == 0 && turn.Attachment is { } attachment)
                asked = string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.AttachedSpreadsheet", culture), Quoted(attachment.Name));
            var answer = turn.Answer?.Text ?? "";
            // A turn closed with assistant_proposal@2 shows each of its proposals, in order.
            IReadOnlyList<AssistantObservedProposal> shown = !latest ? [] : turn.ObservedProposals.Count != 0 ? turn.ObservedProposals : [turn.ObservedProposal!];
            foreach (var proposal in shown)
                answer += (answer.Length == 0 ? "" : "\n\n") + string.Format(CultureInfo.InvariantCulture, AssistantTexts.Get("Prompt.PreviousProposal", culture),
                    JsonSerializer.Serialize(proposal.Payload, proposal.Payload.GetType(), AiAssistantResponsePolicy.JsonOptions));
            var length = asked.Length + answer.Length;
            if (used + length > historyCharacters) continue;
            used += length;
            messages.Add(new("user", asked));
            messages.Add(new("assistant", answer));
        }
        return messages;
    }

    private static string Quoted(string name) => JsonSerializer.Serialize(name, AiAssistantResponsePolicy.JsonOptions);
}
