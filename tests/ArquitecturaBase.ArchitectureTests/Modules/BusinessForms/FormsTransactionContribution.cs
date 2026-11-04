using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.ArchitectureTests.Support;
namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;
internal sealed class FormsTransactionContribution : IModuleArchitectureContribution
{
    public IReadOnlyCollection<Type> CapabilityReadPorts =>
        [typeof(IFormPersonDataReader), typeof(IFormSelectionReader), typeof(IFormMissingRequestReader), typeof(IFormResponseReader), typeof(IFormSuggestionReader), typeof(IFormCatalogReader), typeof(IFormCatalogFilterReader), typeof(IFormRuleReader), typeof(IFormPdfDesignReader), typeof(IFormAuthoringReader)];
    public IReadOnlyCollection<string> LockOwners => ["ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.FormAdvisoryLockKeys"];
    public IReadOnlyCollection<string> LockPrefixes => ["forms:"];
    public IReadOnlyCollection<string> BulkUpdateOwners => [];
    // FormRecordReadBatch es un lote de filas (FormRecordStoredRow y FormResponseVersionRow), no una fila.
    // StructuredFormVersionReference es el modelo neutral del puerto de retiro, que el lector devuelve tal cual.
    public IReadOnlyCollection<Type> ReaderOutputExceptions => [typeof(FormRecordReadBatch), typeof(StructuredFormVersionReference)];
}
