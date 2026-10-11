using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormSelectionReader
{
    Task<StructuredFormSelectionRow?> FindAsync(Guid definitionId, Guid versionId, CancellationToken cancellationToken);
    Task<StructuredFormSelectionRow?> FindPublishedAsync(Guid definitionId, Guid versionId, CancellationToken cancellationToken);
    Task<PagedResult<StructuredFormSelectionRow>> ListAsync(string recipientCulture, ListStructuredFormsRequest request, CancellationToken cancellationToken);
}
