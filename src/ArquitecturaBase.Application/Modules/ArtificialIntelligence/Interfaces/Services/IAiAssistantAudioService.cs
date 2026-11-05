using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

/// <summary>Home recordings (IA4): keep the private copy, normalize it, say how it goes and let its person listen to it.</summary>
public interface IAiAssistantAudioService
{
    Task<Result<AiAssistantAudioResponse>> SubmitAsync(SubmitAiAssistantAudioRequest request, CancellationToken cancellationToken);
    Task<Result<AiAssistantAudioResponse>> GetAsync(Guid requestId, string culture, CancellationToken cancellationToken);
    Task<Result<AiAssistantAudioFileResponse>> OpenAsync(Guid requestId, CancellationToken cancellationToken);
}
