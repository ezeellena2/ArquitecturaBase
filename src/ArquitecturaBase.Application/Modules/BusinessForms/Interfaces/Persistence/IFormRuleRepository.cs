using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormRuleRepository
{
    Task<BusinessFormRuleSet?> GetAsync(Guid id, CancellationToken cancellationToken);
    void Add(BusinessFormRuleSet ruleSet);
}
