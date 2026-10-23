using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.ArchitectureTests.Support;
namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;
internal sealed class FormsTransactionContribution : IModuleArchitectureContribution
{
    public IReadOnlyCollection<Type> CapabilityReadPorts =>
        [typeof(IFormPersonDataReader), typeof(IFormSelectionReader), typeof(IFormMissingRequestReader), typeof(IFormResponseReader), typeof(IFormSuggestionReader), typeof(IFormCatalogReader), typeof(IFormCatalogFilterReader), typeof(IFormRuleReader), typeof(IFormPdfDesignReader)];
    public IReadOnlyCollection<string> LockOwners => ["ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.FormAdvisoryLockKeys"];
    public IReadOnlyCollection<string> LockPrefixes => ["forms:"];
    public IReadOnlyCollection<string> BulkUpdateOwners => [];
}
