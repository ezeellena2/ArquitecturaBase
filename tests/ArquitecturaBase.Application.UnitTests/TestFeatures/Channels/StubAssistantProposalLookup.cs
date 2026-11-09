using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;

/// <summary>Answers with the configured Home proposal only for its owner, request and id; anything else is unavailable.</summary>
internal sealed class StubAssistantProposalLookup : IAssistantProposalLookup
{
    public AssistantProposalLookupResponse? Response { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public int Reads { get; private set; }
    public IReadOnlyCollection<string>? LastCapabilityGroup { get; private set; }

    public Task<Result<AssistantProposalLookupResponse>> FindAsync(Guid actorUserId, Guid requestId, Guid proposalId,
        IReadOnlyCollection<string> capabilityGroup, CancellationToken cancellationToken)
    {
        Reads++;
        LastCapabilityGroup = capabilityGroup;
        return Task.FromResult<Result<AssistantProposalLookupResponse>>(Response is { } response && actorUserId == OwnerUserId
            && requestId == RequestId && response.Proposal.Id == proposalId ? response : AssistantOperationErrors.Unavailable);
    }
}
