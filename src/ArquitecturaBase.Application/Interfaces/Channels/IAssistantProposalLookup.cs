using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>A proposal the actor saw in their own Home conversation, for its owner to create it when the person asks.</summary>
public interface IAssistantProposalLookup
{
    Task<Result<AssistantProposalLookupResponse>> FindAsync(Guid actorUserId, Guid requestId, Guid proposalId, CancellationToken cancellationToken);
}
