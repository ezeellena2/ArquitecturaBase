using ArquitecturaBase.ArchitectureTests.Support;
namespace ArquitecturaBase.ArchitectureTests.Modules.ArtificialIntelligence;
internal sealed class AiTransactionContribution : IModuleArchitectureContribution
{
    public IReadOnlyCollection<string> LockOwners => ["ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.AiAdvisoryLockKeys"];
    public IReadOnlyCollection<string> LockPrefixes => ["ai-connection:", "ai-business-information:", "ai-assistant-budget:", "ai-assistant-actor:", "ai-assistant-conversation:"];
    public IReadOnlyCollection<string> BulkUpdateOwners => [];
}
