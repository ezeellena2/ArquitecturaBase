using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>The Home proposals a test hands out, by owner, request and id; anything else is unavailable, as an expired turn.</summary>
internal sealed class ScriptedAssistantProposalLookup : IAssistantProposalLookup
{
    private readonly ConcurrentDictionary<(Guid Owner, Guid Request, Guid Proposal), AssistantProposalLookupResponse> proposals = new();
    private int reads;

    public int Reads => Volatile.Read(ref reads);

    public void Add(Guid ownerUserId, Guid requestId, AssistantProposalLookupResponse response) =>
        proposals[(ownerUserId, requestId, response.Proposal.Id)] = response;

    public Task<Result<AssistantProposalLookupResponse>> FindAsync(Guid actorUserId, Guid requestId, Guid proposalId, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref reads);
        return Task.FromResult<Result<AssistantProposalLookupResponse>>(proposals.TryGetValue((actorUserId, requestId, proposalId), out var response)
            ? response : AssistantOperationErrors.Unavailable);
    }
}
