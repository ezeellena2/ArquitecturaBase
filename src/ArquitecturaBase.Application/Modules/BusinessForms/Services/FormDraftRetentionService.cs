using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormDraftRetentionService(IFormDraftRepository repository, IUnitOfWork unitOfWork, TimeProvider timeProvider) : IFormDraftRetentionService
{
    public Task<Result> PurgeExpiredResultsAsync(CancellationToken cancellationToken) => unitOfWork.ExecuteInTransactionAsync(async ct =>
    {
        await repository.LockDefinitionsAsync(ct);
        await repository.UpdateExpiredResultsAsync(timeProvider.GetUtcNow().UtcDateTime, ct);
        return Result.Success();
    }, CommitPolicy.OnSuccess, cancellationToken);
}
