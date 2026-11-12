using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

/// <summary>A Home turn that brings a spreadsheet (Stage 2) or a Word (Stage 3): read it through its owner, keep its private copy and accept the turn with it.</summary>
public interface IAiAssistantFileTurnService
{
    Task<Result<AiAssistantTurnAcceptedResponse>> SubmitAsync(SubmitAiAssistantFileTurnRequest request, CancellationToken cancellationToken);
}
