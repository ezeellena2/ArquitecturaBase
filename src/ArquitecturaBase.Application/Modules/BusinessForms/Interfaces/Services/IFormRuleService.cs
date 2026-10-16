using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormRuleService
{
    Task<Result<FormRuleSetResponse>> GetCurrentAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<Result<PagedResult<FormRuleRevisionResponse>>> ListRevisionsAsync(Guid definitionId, ListFormRuleRevisionsRequest request, CancellationToken cancellationToken);
    Task<Result<FormRuleSetResponse>> GetRevisionAsync(Guid definitionId, long revision, CancellationToken cancellationToken);
    Task<Result<FormRuleSetResponse>> SaveAsync(Guid definitionId, SaveFormRulesRequest request, CancellationToken cancellationToken);
    Task<Result<FormRuleOptionsResponse>> GetOptionsAsync(Guid definitionId, CancellationToken cancellationToken);
}
