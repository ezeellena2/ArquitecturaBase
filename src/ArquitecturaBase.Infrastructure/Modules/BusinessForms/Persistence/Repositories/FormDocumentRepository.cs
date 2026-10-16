using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
internal sealed class FormDocumentRepository(ApplicationDbContext dbContext) : IFormDocumentRepository
{
    public void Add(BusinessFormDocument document) => dbContext.Set<BusinessFormDocument>().Add(document);
    public async Task<IReadOnlyList<BusinessFormDocument>> ListForRunAsync(Guid runId, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormDocument>().Where(document => document.RunId == runId).ToListAsync(cancellationToken);
}
