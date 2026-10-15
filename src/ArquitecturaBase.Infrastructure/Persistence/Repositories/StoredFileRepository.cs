using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class StoredFileRepository(ApplicationDbContext context) : IStoredFileRepository
{
    public void Add(StoredFile file) { context.RequireTransaction(); context.Set<StoredFile>().Add(file); }
}
