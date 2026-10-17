using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormSuggestionRepository
{
    Task<BusinessFormSuggestionSet?> GetAsync(Guid id,CancellationToken cancellationToken);
    void Add(BusinessFormSuggestionSet suggestionSet);
}
