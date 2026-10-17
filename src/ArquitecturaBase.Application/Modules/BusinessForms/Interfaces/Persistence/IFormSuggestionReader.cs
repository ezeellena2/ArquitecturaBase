using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormSuggestionReader
{
    Task<bool> ExistsDefinitionAsync(Guid definitionId,CancellationToken cancellationToken);
    Task<FormSuggestionSetStoredRow?> FindCurrentAsync(Guid definitionId,CancellationToken cancellationToken);
    Task<FormSuggestionSetStoredRow?> FindRevisionAsync(Guid definitionId,long revision,CancellationToken cancellationToken);
    Task<FormSuggestionOptionsResponse?> FindOptionsAsync(Guid definitionId,CancellationToken cancellationToken);
    Task<PagedResult<FormSuggestionRevisionStoredRow>> ListRevisionsAsync(Guid definitionId,ListFormSuggestionRevisionsRequest request,CancellationToken cancellationToken);
}
