using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
internal sealed class FormDraftRepository(ApplicationDbContext dbContext) : IFormDraftRepository
{
    public Task LockRequestAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Request(actor, requestId)], cancellationToken);
    public Task LockDefinitionsAsync(CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Definitions], cancellationToken);
    public Task<BusinessFormDefinition?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormDefinition>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<BusinessFormDraftChange?> GetChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDraftChange>().SingleOrDefaultAsync(x => x.OperatorUserId == actor && x.RequestId == requestId, cancellationToken);
    public Task<bool> ExistsPrefixAsync(string prefix, Guid exceptId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDefinition>().AnyAsync(x => x.Prefix == prefix && x.Id != exceptId, cancellationToken);
    public void Add(BusinessFormDefinition definition) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormDefinition>().Add(definition); }
    public void AddChange(BusinessFormDraftChange change) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormDraftChange>().Add(change); }
    public async Task UpdateExpiredResultsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var expired = await dbContext.Set<BusinessFormDraftChange>().Where(x => x.ProtectedResult != null && x.ResultExpiresAtUtc <= nowUtc)
            .OrderBy(x => x.ResultExpiresAtUtc).ThenBy(x => x.Id).Take(100).ToListAsync(cancellationToken);
        foreach (var change in expired) change.ExpireResult(nowUtc);
    }
}
