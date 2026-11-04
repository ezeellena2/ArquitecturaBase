using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>Creates the catalog a Home proposal describes, once, and says whether that proposal already created one.</summary>
public interface IFormCatalogAssistantProposalService
{
    Task<Result<FormCatalogAssistantProposalResponse>> CreateAsync(CreateFromAssistantProposalRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogAssistantProposalResponse>> GetAsync(Guid proposalId, CancellationToken cancellationToken);
}
