using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Channels;

internal sealed class DisabledAssistantProposalLookup : IAssistantProposalLookup
{
    public Task<Result<AssistantProposalLookupResponse>> FindAsync(Guid actorUserId, Guid requestId, Guid proposalId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<AssistantProposalLookupResponse>>(AssistantOperationErrors.Unavailable);
    }
}
