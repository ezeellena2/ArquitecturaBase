using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «Crear borrador» of DOC6: the draft of a request's proposal, as the reviewer edited it, with the reading on, the proposal's provenance as
/// its evidence and its documents recorded; once per request.
/// </summary>
public interface IFormDocumentDraftService
{
    Task<Result<FormDraftAssistantProposalResponse>> CreateAsync(CreateFormDocumentDraftRequest request, CancellationToken cancellationToken);
}
