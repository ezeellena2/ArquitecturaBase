using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Interfaces.Services;
public interface IPrivateFileCleanupService
{
    Task<Result> PurgeAsync(CancellationToken cancellationToken);
}
