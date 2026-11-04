using ArquitecturaBase.ArchitectureTests.Support;

namespace ArquitecturaBase.ArchitectureTests.Modules.Arca;

internal sealed class ArcaTransactionContribution : IModuleArchitectureContribution
{
    public IReadOnlyCollection<string> LockOwners => ["ArquitecturaBase.Infrastructure.Modules.Arca.Persistence.ArcaLockKeys"];
    public IReadOnlyCollection<string> LockPrefixes => ["arca-connection:", "arca-ticket:"];
    public IReadOnlyCollection<string> BulkUpdateOwners => [];
}
