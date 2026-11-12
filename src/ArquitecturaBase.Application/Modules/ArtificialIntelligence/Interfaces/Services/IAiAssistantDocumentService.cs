using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

/// <summary>Home photos and PDF files (Stage 3): keep the private copy, read it and say how it goes, only to its person.</summary>
public interface IAiAssistantDocumentService
{
    Task<Result<AiAssistantDocumentResponse>> SubmitAsync(SubmitAiAssistantDocumentRequest request, CancellationToken cancellationToken);
    Task<Result<AiAssistantDocumentResponse>> GetAsync(Guid requestId, string culture, CancellationToken cancellationToken);
}
