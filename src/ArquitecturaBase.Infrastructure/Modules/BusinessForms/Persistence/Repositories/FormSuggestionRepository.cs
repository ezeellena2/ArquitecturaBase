using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
internal sealed class FormSuggestionRepository(ApplicationDbContext dbContext) : IFormSuggestionRepository
{
    public Task<BusinessFormSuggestionSet?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormSuggestionSet>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
    public void Add(BusinessFormSuggestionSet suggestionSet) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormSuggestionSet>().Add(suggestionSet); }
}
