using ArquitecturaBase.Domain.Files;
namespace ArquitecturaBase.Application.Interfaces.Persistence;

public interface IStoredFileRepository
{
    void Add(StoredFile file);
}
