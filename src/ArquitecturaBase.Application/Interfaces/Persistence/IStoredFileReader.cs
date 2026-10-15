using ArquitecturaBase.Application.Models.Files;
namespace ArquitecturaBase.Application.Interfaces.Persistence;

public interface IStoredFileReader
{
    Task<StoredFileRow?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys, CancellationToken cancellationToken);
}
