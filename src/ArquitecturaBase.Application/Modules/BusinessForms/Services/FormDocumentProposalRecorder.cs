using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The part of DOC6 that runs inside the draft's save (<c>FormDraftService</c>, under the definitions lock): the draft a request created,
/// once, under the request's lock, so its documents are the draft's source documents. It never saves.
/// </summary>
internal sealed class FormDocumentProposalRecorder(IFormDocumentProposalRepository proposals)
{
    public Func<BusinessFormDefinition, CancellationToken, Task<Result>> Draft(Guid proposalId, Guid observedProposalId) => async (definition, ct) =>
    {
        await proposals.LockAsync(proposalId, ct);
        return await proposals.GetAsync(proposalId, ct) is { } proposal
            ? proposal.Draft(definition.Id, observedProposalId) : FormReceivedDocumentErrors.ProposalNotFound;
    };
}
