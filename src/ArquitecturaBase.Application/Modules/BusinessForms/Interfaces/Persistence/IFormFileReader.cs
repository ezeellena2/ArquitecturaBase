using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormFileReader
{
    Task<FormFileAccessRow?> FindAsync(Guid recordId, Guid fileId, CancellationToken cancellationToken);
}
