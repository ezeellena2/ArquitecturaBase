using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FormPdfDocumentReaderDouble : IFormPdfDesignReader
{
    public FormPdfDocumentRow? Document { get; set; }
    public int Reads { get; private set; }
    public Task<FormPdfDocumentRow?> FindDocumentAsync(Guid recordId, Guid documentId, CancellationToken cancellationToken)
    {
        Reads++; return Task.FromResult(Document is { } row && row.RecordId == recordId && row.Id == documentId ? row : null);
    }
    public Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<FormPdfDesignStoredRow?> FindCurrentAsync(Guid definitionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<FormPdfDesignStoredRow?> FindRevisionAsync(Guid definitionId, long revision, CancellationToken cancellationToken) => throw new NotSupportedException();
}
