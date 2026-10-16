using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Services.Files;
internal sealed class PrivateFileCleanupService(IPrivateFileStore store, IStoredFileReader files, TimeProvider timeProvider,
    ILogger<PrivateFileCleanupService> logger, IEnumerable<IPrivateFileRetentionSource> retentionSources) : IPrivateFileCleanupService
{
    public Task<Result> PurgeAsync(CancellationToken cancellationToken) => OperationLog.RunAsync(logger, "PurgeUnreferencedPrivateFiles", async () =>
    {
        if (!store.IsAvailable) return Result.Success();
        // Captures retry for minutes; a full day leaves ongoing uploads outside this sweep.
        var cutoff = timeProvider.GetUtcNow().UtcDateTime.AddDays(-1);
        var batch = new List<string>(128);
        await foreach (var blob in store.ListAsync(cutoff, cancellationToken))
        {
            batch.Add(blob.Key);
            if (batch.Count < 128) continue;
            await PurgeBatchAsync(batch, cancellationToken);
            batch.Clear();
        }
        if (batch.Count > 0) await PurgeBatchAsync(batch, cancellationToken);
        return Result.Success();
    });
    private async Task PurgeBatchAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        var referenced = (await files.ListReferencedKeysAsync(keys, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        foreach (var source in retentionSources)
            referenced.UnionWith(await source.ListReferencedKeysAsync(keys, cancellationToken));
        foreach (var key in keys)
            if (!referenced.Contains(key)) await store.DeleteAsync(key, cancellationToken);
    }

}
