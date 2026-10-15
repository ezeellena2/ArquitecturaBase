using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Files;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
internal sealed class MemoryStoredFileRepository : IStoredFileRepository
{
    public List<StoredFile> Files { get; } = [];
    public void Add(StoredFile file) => Files.Add(file);
}
