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
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken)
    {
        // The current writer derives thumbnails from the original key; query only the indexed StorageKey.
        const string suffix = ".thumb.jpg";
        var originals = storageKeys.Select(key => key.EndsWith(suffix, StringComparison.Ordinal) ? key[..^suffix.Length] : key).Distinct().ToArray();
        var rows = await context.Set<StoredFile>().AsNoTracking().Where(file => originals.Contains(file.StorageKey))
            .Select(file => new { file.StorageKey, file.ThumbnailKey }).ToListAsync(cancellationToken);
        var requested = storageKeys.ToHashSet(StringComparer.Ordinal);
        return rows.SelectMany(row => row.ThumbnailKey is null ? new[] { row.StorageKey } : new[] { row.StorageKey, row.ThumbnailKey })
            .Where(requested.Contains).Distinct(StringComparer.Ordinal).ToArray();
    }
}
