using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormActionRecipientReader
{
    Task<FormActionRecipientRow?> FindAsync(Guid userId,CancellationToken cancellationToken);
    Task<IReadOnlyList<FormActionRecipientRow>> ListForRoleAsync(Guid roleId,CancellationToken cancellationToken);
}
