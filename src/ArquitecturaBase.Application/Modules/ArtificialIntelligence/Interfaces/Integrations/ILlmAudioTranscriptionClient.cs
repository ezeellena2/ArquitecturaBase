using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

/// <summary>Technical provider transport. Only the durable AI normalization stage may consume it.</summary>
public interface ILlmAudioTranscriptionClient
{
    Task<Result<LlmAudioTranscriptionResponse>> TranscribeAsync(
        LlmAudioTranscriptionRequest request, CancellationToken cancellationToken);
}
