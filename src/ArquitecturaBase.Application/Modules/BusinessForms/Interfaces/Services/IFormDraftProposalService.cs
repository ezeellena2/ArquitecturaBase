using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>Creates the draft a Home or channel proposal describes, once, and says whether that proposal already created one.</summary>
public interface IFormDraftProposalService
{
    Task<Result<FormDraftAssistantProposalResponse>> CreateAsync(CreateFromAssistantProposalRequest request, CancellationToken cancellationToken);
    Task<Result<FormDraftAssistantProposalResponse>> CreateFromChannelAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken);
    /// <summary>DOC6: the proposal of a request from received documents, as the reviewer edited it; the inbox checked the request and its proposal.</summary>
    Task<Result<FormDraftAssistantProposalResponse>> CreateFromDocumentsAsync(CreateFromDocumentProposalRequest request, CancellationToken cancellationToken);
    Task<Result<FormDraftAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken);
}
