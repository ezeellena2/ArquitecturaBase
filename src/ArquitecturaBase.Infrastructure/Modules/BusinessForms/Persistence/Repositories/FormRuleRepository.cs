using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
internal sealed class FormRuleRepository(ApplicationDbContext dbContext) : IFormRuleRepository
{
    public Task<BusinessFormRuleSet?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormRuleSet>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
    public void Add(BusinessFormRuleSet ruleSet) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormRuleSet>().Add(ruleSet); }
}
