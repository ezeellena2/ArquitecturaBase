using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>A proposal the actor saw in their own Home conversation, for its owner to create it when the person asks.</summary>
public interface IAssistantProposalLookup
{
    /// <summary>
    /// <paramref name="capabilityGroup"/> names the capabilities whose proposals replace each other: a later proposal of any of them
    /// replaces one of the group. A proposal outside the group is replaced only by a later one of its own capability.
    /// </summary>
    Task<Result<AssistantProposalLookupResponse>> FindAsync(Guid actorUserId, Guid requestId, Guid proposalId,
        IReadOnlyCollection<string> capabilityGroup, CancellationToken cancellationToken);
}
