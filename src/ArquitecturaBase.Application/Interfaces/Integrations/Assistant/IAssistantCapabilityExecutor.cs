using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Integrations.Assistant;

/// <summary>One isolated technical scope per local capability execution, without a write boundary.</summary>
public interface IAssistantCapabilityExecutor
{
    Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken);
}
