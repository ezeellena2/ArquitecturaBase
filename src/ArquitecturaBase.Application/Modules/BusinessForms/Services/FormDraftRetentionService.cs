using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormDraftRetentionService(IFormDraftRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    ILogger<FormDraftRetentionService> logger) : IFormDraftRetentionService
{
    public Task<Result> PurgeExpiredResultsAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "PurgeExpiredFormDraftResults", () => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockDefinitionsAsync(ct);
            await repository.UpdateExpiredResultsAsync(timeProvider.GetUtcNow().UtcDateTime, ct);
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken));
}
