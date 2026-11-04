using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantTerminalPolicy
{
    public static Result<AssistantObservedProposal> ValidateProposal(AssistantChannelContext context, Guid turnId,
        string terminalId, string argumentsJson, IReadOnlyList<AssistantObservedProposal> observed)
    {
        if (terminalId != "assistant_proposal" || !context.AllowedTerminals.Contains(new(terminalId, 1))) return AiAssistantErrors.ToolRejected;
        try
        {
            using var json = JsonDocument.Parse(argumentsJson);
            var root = json.RootElement;
            if (turnId == Guid.Empty || root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 ||
                !root.TryGetProperty("proposalId", out var property) || property.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(property.GetString(), out var proposalId) || proposalId == Guid.Empty) return AiAssistantErrors.InvalidOutput;
            return SelectObserved(context, turnId, proposalId, observed);
        }
        catch (JsonException) { return AiAssistantErrors.InvalidOutput; }
    }

    // Exactly one proposal observed in this same turn, from a capability the channel offered.
    public static Result<AssistantObservedProposal> SelectObserved(AssistantChannelContext context, Guid turnId, Guid proposalId,
        IReadOnlyList<AssistantObservedProposal> observed)
    {
        if (turnId == Guid.Empty || proposalId == Guid.Empty) return AiAssistantErrors.InvalidOutput;
        var selected = observed.Where(proposal => proposal.Id == proposalId).ToArray();
        return selected.Length != 1 || selected[0].TurnId != turnId || !context.AllowedCapabilities.Contains(selected[0].Capability)
            ? AiAssistantErrors.InvalidOutput : selected[0];
    }
}
