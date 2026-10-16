using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormPdfDesignReader
{
    Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<FormPdfDesignStoredRow?> FindCurrentAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<FormPdfDesignStoredRow?> FindRevisionAsync(Guid definitionId, long revision, CancellationToken cancellationToken);
    Task<FormPdfDocumentRow?> FindDocumentAsync(Guid recordId, Guid documentId, CancellationToken cancellationToken);
}
