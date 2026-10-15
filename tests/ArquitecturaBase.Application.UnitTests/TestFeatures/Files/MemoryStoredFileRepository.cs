using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Application.Models.Files;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
internal sealed class MemoryStoredFileRepository : IStoredFileRepository, IStoredFileReader
{
    public List<StoredFile> Files { get; } = [];
    public void Add(StoredFile file) => Files.Add(file);
    public Task<StoredFileRow?> FindAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = Files.SingleOrDefault(value => value.Id == id);
        return Task.FromResult(file is null ? null : new StoredFileRow(file.Id, file.OwnerUserId, file.Name, file.ContentType, file.Size, file.Sha256, file.StorageKey, file.ThumbnailKey));
    }
    public Task<bool> ExistsKeyAsync(string storageKey, CancellationToken cancellationToken) => Task.FromResult(Files.Any(file => file.StorageKey == storageKey || file.ThumbnailKey == storageKey));
}
