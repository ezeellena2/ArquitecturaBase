using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormSuggestionService
{
    Task<Result<FormSuggestionSetResponse>> GetCurrentAsync(Guid definitionId,CancellationToken cancellationToken);
    Task<Result<FormSuggestionSetResponse>> SaveAsync(Guid definitionId,SaveFormSuggestionsRequest request,CancellationToken cancellationToken);
    Task<Result<PagedResult<FormSuggestionRevisionResponse>>> ListRevisionsAsync(Guid definitionId,ListFormSuggestionRevisionsRequest request,CancellationToken cancellationToken);
    Task<Result<FormSuggestionSetResponse>> GetRevisionAsync(Guid definitionId,long revision,CancellationToken cancellationToken);
    Task<Result<FormSuggestionOptionsResponse>> GetOptionsAsync(Guid definitionId,CancellationToken cancellationToken);
}
