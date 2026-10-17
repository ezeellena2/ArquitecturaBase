using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>Versioned local reads/proposals; no persistence or external effects are allowed.</summary>
public interface IAssistantCapabilitySource
{
    IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; }
    Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken);
    Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson);
}
