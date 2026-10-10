using ArquitecturaBase.ArchitectureTests.Support;
namespace ArquitecturaBase.ArchitectureTests.Modules.BusinessForms;
internal sealed class FormsTransactionContribution : IModuleArchitectureContribution
{
    public IReadOnlyCollection<string> LockOwners => ["ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.FormAdvisoryLockKeys"];
    public IReadOnlyCollection<string> LockPrefixes => ["forms:"];
    public IReadOnlyCollection<string> BulkUpdateOwners => [];
}
