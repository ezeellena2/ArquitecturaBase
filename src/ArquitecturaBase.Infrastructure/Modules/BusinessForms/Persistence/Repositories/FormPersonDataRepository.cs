using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormPersonDataRepository(ApplicationDbContext dbContext) : IFormPersonDataRepository
{
    public Task LockDefinitionsAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Definitions], cancellationToken);

    public Task LockAttributesAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.PersonAttributes], cancellationToken);

    public Task LockDataAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.PersonData(userId)], cancellationToken);

    public Task<BusinessFormPersonAttribute?> GetAttributeAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPersonAttribute>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

    public Task<BusinessFormPersonData?> GetDataAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPersonData>().SingleOrDefaultAsync(value => value.UserId == userId, cancellationToken);

    public Task<List<BusinessFormPersonAttribute>> ListAttributesAsync(CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPersonAttribute>().OrderBy(value => value.Position).ThenBy(value => value.Id).ToListAsync(cancellationToken);

    public void Add(BusinessFormPersonAttribute value)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormPersonAttribute>().Add(value);
    }

    public void Add(BusinessFormPersonData value)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormPersonData>().Add(value);
    }
}
