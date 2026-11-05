using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;

namespace ArquitecturaBase.Application.Channels;

internal sealed class UnavailableStructuredAuthoringProposalChannel : IStructuredAuthoringProposalChannel
{
    public StructuredAuthoringSummary? Summarize(AssistantObservedProposal proposal, string culture) => null;

    public Task<Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<Result<StructuredAuthoringCreatedResponse>>(SystemQueryErrors.NotFound);
    }
}
