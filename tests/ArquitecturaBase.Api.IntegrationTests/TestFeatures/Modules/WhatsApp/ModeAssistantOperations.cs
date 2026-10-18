using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class ModeAssistantOperations(string availability) : IAssistantOperations
{
    public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct) => Task.FromResult(new AssistantAvailabilityResponse(availability));
    public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) => Task.FromResult(Result.Success());
    public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
}
