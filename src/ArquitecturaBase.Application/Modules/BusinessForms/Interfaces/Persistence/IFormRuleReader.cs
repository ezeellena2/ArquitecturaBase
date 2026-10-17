using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormRuleReader
{
    Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<FormRuleSetStoredRow?> FindCurrentAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<FormRuleSetStoredRow?> FindRevisionAsync(Guid definitionId, long revision, CancellationToken cancellationToken);
    Task<PagedResult<FormRuleRevisionStoredRow>> ListRevisionsAsync(Guid definitionId, ListFormRuleRevisionsRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormRuleRoleResponse>> ListRolesAsync(CancellationToken cancellationToken);
}
