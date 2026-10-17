using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormDocumentRepository(ApplicationDbContext dbContext) : IFormDocumentRepository
{
    public void Add(BusinessFormDocument document) => dbContext.Set<BusinessFormDocument>().Add(document);
}
