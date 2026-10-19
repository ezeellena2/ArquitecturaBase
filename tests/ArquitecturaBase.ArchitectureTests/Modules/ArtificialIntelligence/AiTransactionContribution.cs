using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.ArchitectureTests.Support;
namespace ArquitecturaBase.ArchitectureTests.Modules.ArtificialIntelligence;
internal sealed class AiTransactionContribution : IModuleArchitectureContribution
{
    public IReadOnlyCollection<Type> CapabilityReadPorts =>
        [typeof(IAiBusinessInformationReader)];
    public IReadOnlyCollection<string> LockOwners => ["ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.AiAdvisoryLockKeys"];
    public IReadOnlyCollection<string> LockPrefixes => ["ai-connection:", "ai-business-information:", "ai-assistant-budget:", "ai-assistant-actor:", "ai-assistant-conversation:"];
    public IReadOnlyCollection<string> BulkUpdateOwners => [];
    public IReadOnlyCollection<string> AdditionalUseCaseEntryPoints =>
        ["ArquitecturaBase.Application.Modules.ArtificialIntelligence.Channels.AiAssistantOperations"];
}
