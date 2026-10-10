using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormDraftRetentionService
{
    Task<Result> PurgeExpiredResultsAsync(CancellationToken cancellationToken);
}
