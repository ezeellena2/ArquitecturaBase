using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantHistoryPolicy
{
    // Only the latest proposal travels with its content, so a requested change can be proposed again in full; older ones were
    // replaced and keep only their explanation. A channel keeps no explanation, so its latest proposal travels alone and the older
    // ones are left out. The latest proposal is reserved first; the rest fills from the newest turn to the oldest within the
    // character budget, up to the first that does not fit, and everything goes in chronological order. A latest proposal that alone
    // exceeds the budget fails the turn visibly (B1): the model never receives a request to change a proposal it cannot see.
    public static Result<IReadOnlyList<LlmConversationMessage>> Messages(IReadOnlyList<AiAssistantTurnResponse> newestFirst, int historyCharacters, string culture)
    {
        var latestProposal = newestFirst.FirstOrDefault(turn => turn.ObservedProposal is not null);
        var reserved = latestProposal?.Question is null ? ((string Asked, string Answer)?)null : Exchange(latestProposal, true, culture);
        var used = reserved is { } proposal ? proposal.Asked.Length + proposal.Answer.Length : 0;
        if (used > historyCharacters) return AiAssistantErrors.ProposalTooLarge;
        var kept = new List<(string Asked, string Answer)>();
        var full = false;
        foreach (var turn in newestFirst)
        {
            if (ReferenceEquals(turn, latestProposal))
            {
                if (reserved is { } own) kept.Add(own);
                continue;
            }
            if (full || turn.Question is null || turn.Answer is null) continue;
            var exchange = Exchange(turn, false, culture);
            var length = exchange.Asked.Length + exchange.Answer.Length;
            if (used + length > historyCharacters)
            {
                full = true;
                continue;
            }
            used += length;
            kept.Add(exchange);
        }
        kept.Reverse();
        return Result.Success<IReadOnlyList<LlmConversationMessage>>(
            [.. kept.SelectMany(exchange => new LlmConversationMessage[] { new("user", exchange.Asked), new("assistant", exchange.Answer) })]);
    }

    private static (string Asked, string Answer) Exchange(AiAssistantTurnResponse turn, bool latest, string culture)
    {
        var asked = AiAssistantAudioPolicy.Spoken(turn.Question!, turn.Audio?.Transcript);
        // A turn that brought only a file is never an empty message, which a provider may reject; the file itself travels in the
        // current message, so only its name goes here, as a JSON string: the person chose it, so it never goes raw in their message.
        if (asked.Trim().Length == 0 && turn.AttachedDocument is { } document)
            asked = string.Format(CultureInfo.InvariantCulture, AssistantTexts.Prompt("Prompt.AttachedDocument", culture), Quoted(document));
        else if (asked.Trim().Length == 0 && turn.Attachment is { } attachment)
            asked = string.Format(CultureInfo.InvariantCulture, AssistantTexts.Prompt("Prompt.AttachedSpreadsheet", culture), Quoted(attachment.Name));
        var answer = turn.Answer?.Text ?? "";
        // A turn closed with assistant_proposal@2 shows each of its proposals, in order.
        IReadOnlyList<AssistantObservedProposal> shown = !latest ? [] : turn.ObservedProposals.Count != 0 ? turn.ObservedProposals : [turn.ObservedProposal!];
        foreach (var proposal in shown)
            answer += (answer.Length == 0 ? "" : "\n\n") + string.Format(CultureInfo.InvariantCulture, AssistantTexts.Prompt("Prompt.PreviousProposal", culture),
                JsonSerializer.Serialize(proposal.Payload, proposal.Payload.GetType(), AiAssistantResponsePolicy.JsonOptions));
        return (asked, answer);
    }

    private static string Quoted(string name) => JsonSerializer.Serialize(name, AiAssistantResponsePolicy.JsonOptions);
}
