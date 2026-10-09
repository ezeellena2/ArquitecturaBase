using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

public interface IAiAssistantRetentionService
{
    Task<Result<int>> ReconcileExpiredBatchAsync(CancellationToken cancellationToken);
    Task<Result<int>> PurgeBatchAsync(CancellationToken cancellationToken);
}
