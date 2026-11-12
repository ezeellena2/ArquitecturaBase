using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

/// <summary>Technical provider transport. Only the durable AI document reading stage may consume it.</summary>
public interface ILlmDocumentReadingClient
{
    Task<Result<LlmDocumentReadingResponse>> ReadAsync(LlmDocumentReadingRequest request, CancellationToken cancellationToken);
}
