using ArquitecturaBase.Application.Models.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantEvidenceVerifier
{
    public static Result Verify(AiAssistantAnswer? answer, IReadOnlyList<SystemQueryEvidence> evidences)
    {
        if (answer is null || answer.Kind is not ("answer" or "clarification" or "unsupported") ||
            string.IsNullOrWhiteSpace(answer.Text) || answer.Text.Length > 12000) return AiAssistantErrors.InvalidOutput;
        if (answer.EvidenceIds.Distinct().Count() != answer.EvidenceIds.Count || answer.EvidenceIds.Any(id => !evidences.Any(e => e.Id == id)))
            return AiAssistantErrors.InvalidOutput;
        if (evidences.Select(e => e.Id).Distinct().Count() != evidences.Count) return AiAssistantErrors.InvalidOutput;
        if (answer.Kind == "answer" && answer.EvidenceIds.Count == 0) return AiAssistantErrors.InvalidOutput;
        if (answer.Kind is "clarification" or "unsupported" && answer.Text.Any(char.IsDigit)) return AiAssistantErrors.InvalidOutput;
        return Result.Success();
    }
}
