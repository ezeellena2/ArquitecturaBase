using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Domain.Files;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Persistence.Readers;

internal sealed class StoredFileReader(ApplicationDbContext context) : IStoredFileReader
{
    public Task<StoredFileRow?> FindAsync(Guid id, CancellationToken cancellationToken) => context.Set<StoredFile>().AsNoTracking()
        .Where(file => file.Id == id).Select(file => new StoredFileRow(file.Id, file.OwnerUserId, file.Name,
            file.ContentType, file.Size, file.Sha256, file.StorageKey, file.ThumbnailKey)).SingleOrDefaultAsync(cancellationToken);
    public Task<bool> ExistsKeyAsync(string storageKey, CancellationToken cancellationToken) => context.Set<StoredFile>().AsNoTracking()
        .AnyAsync(file => file.StorageKey == storageKey || file.ThumbnailKey == storageKey, cancellationToken);
}
