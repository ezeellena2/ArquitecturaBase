using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormRecordIssuanceReader(ApplicationDbContext dbContext) : IFormRecordIssuanceReader
{
    public Task<bool> ExistsLiveAsync(Guid recordId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormActionRun>().AsNoTracking().AnyAsync(run => run.RecordId == recordId && run.Kind == "IssueDocument"
            && run.Event != "Voided" && run.State != FormActionRunState.Failed && run.State != FormActionRunState.NotApplicable, cancellationToken);
}
