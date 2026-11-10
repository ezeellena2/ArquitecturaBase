using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormFolderRepository(ApplicationDbContext dbContext) : IFormFolderRepository
{
    public Task LockAsync(CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Folders], cancellationToken);

    // Local holds what was loaded and what this transaction added, and leaves out what it removed.
    public async Task<IReadOnlyList<BusinessFormFolder>> ListAsync(CancellationToken cancellationToken)
    {
        var folders = dbContext.Set<BusinessFormFolder>();
        await folders.LoadAsync(cancellationToken);
        return folders.Local.ToArray();
    }

    public async Task<IReadOnlyList<BusinessFormFolderMembership>> ListMembershipsAsync(FormFolderItemKind kind, Guid itemId,
        CancellationToken cancellationToken)
    {
        var memberships = dbContext.Set<BusinessFormFolderMembership>();
        await memberships.Where(membership => membership.ItemKind == kind && membership.ItemId == itemId).LoadAsync(cancellationToken);
        return memberships.Local.Where(membership => membership.ItemKind == kind && membership.ItemId == itemId).ToArray();
    }

    public void Add(BusinessFormFolder folder)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormFolder>().Add(folder);
    }

    public void Add(BusinessFormFolderMembership membership)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormFolderMembership>().Add(membership);
    }

    // The subfolders loaded by ListAsync follow by cascade in the change tracker, children first; the memberships go by the database
    // cascade.
    public void Remove(BusinessFormFolder folder)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormFolder>().Remove(folder);
    }

    public void Remove(BusinessFormFolderMembership membership)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormFolderMembership>().Remove(membership);
    }
}
