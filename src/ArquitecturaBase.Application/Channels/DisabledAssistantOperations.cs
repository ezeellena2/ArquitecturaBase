using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Channels;

internal sealed class DisabledAssistantOperations : IAssistantOperations
{
    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AssistantAvailabilityResponse("notInstalled"));
    }

    public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<AssistantOperationAcceptedResponse>>(AssistantOperationErrors.Unavailable);
    }

    public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success());
    }

    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<AssistantOperationResponse>>(AssistantOperationErrors.Unavailable);
    }
}
