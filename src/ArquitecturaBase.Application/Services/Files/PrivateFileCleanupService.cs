using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Services.Files;
internal sealed class PrivateFileCleanupService(IPrivateFileStore store, IStoredFileReader files, TimeProvider timeProvider,
    ILogger<PrivateFileCleanupService> logger) : IPrivateFileCleanupService
{
    public Task<Result> PurgeAsync(CancellationToken cancellationToken) => OperationLog.RunAsync(logger, "PurgeUnreferencedPrivateFiles", async () =>
    {
        if (!store.IsAvailable) return Result.Success();
        // Captures retry for minutes; a full day leaves ongoing uploads outside this sweep.
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-1);
        await foreach (var blob in store.ListAsync(cutoff, cancellationToken))
            if (!await files.ExistsKeyAsync(blob.Key, cancellationToken)) await store.DeleteAsync(blob.Key, cancellationToken);
        return Result.Success();
    });
}
