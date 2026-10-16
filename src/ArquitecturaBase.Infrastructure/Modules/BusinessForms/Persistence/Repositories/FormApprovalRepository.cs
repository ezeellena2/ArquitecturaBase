using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormApprovalRepository(ApplicationDbContext dbContext) : IFormApprovalRepository
{
    public Task LockAsync(CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Definitions], cancellationToken);
    public Task<BusinessFormTask?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormTask>().SingleOrDefaultAsync(task => task.Id == id, cancellationToken);
    public Task<BusinessFormRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormRecord>().SingleOrDefaultAsync(record => record.Id == id, cancellationToken);
}
