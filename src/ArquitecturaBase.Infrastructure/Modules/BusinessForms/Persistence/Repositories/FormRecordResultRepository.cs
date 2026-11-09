using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormRecordResultRepository(ApplicationDbContext dbContext) : IFormRecordResultRepository
{
    public void Add(BusinessFormRecordResult result) => dbContext.Set<BusinessFormRecordResult>().Add(result);
}
