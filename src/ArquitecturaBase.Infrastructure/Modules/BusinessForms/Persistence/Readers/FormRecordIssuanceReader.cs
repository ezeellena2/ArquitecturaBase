using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormRecordIssuanceReader(ApplicationDbContext dbContext) : IFormRecordIssuanceReader
{
    public Task<bool> ExistsLiveAsync(Guid recordId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormActionRun>().AsNoTracking().Where(run => run.RecordId == recordId).AnyAsync(BusinessFormActionRun.LiveIssuance, cancellationToken);
}
