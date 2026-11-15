using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox's retention (decision 9, F9): a batch of the proposed documents nobody reviewed for 90 days, discarded as «expired», and a batch of the discarded and unclassified documents whose deletion date passed, each read again
/// under its lock (one reopened meanwhile stays), deleted with its reading, proposal and history; its copy is no longer kept by the
/// retention source, so the cleanup of private files deletes it. A confirmed one follows its response and never expires here.
/// </summary>
internal sealed class FormReceivedDocumentRetentionService(IFormReceivedDocumentReader reader, IFormReceivedDocumentRepository repository,
    IUnitOfWork unitOfWork, TimeProvider clock, ILogger<FormReceivedDocumentRetentionService> logger) : IFormReceivedDocumentRetentionService
{
    public const int BatchSize = 100;
    /// <summary>F40: how long one purge may keep taking batches, so a backlog never holds a worker for good; the next round goes on.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(5);

    // F40: batches follow each other, every one in its own transaction, while they come out full and the time cap allows.
    public Task<Result> PurgeExpiredAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "PurgeExpiredFormReceivedDocuments", async () =>
        {
            var started = clock.GetTimestamp();
            while (true)
            {
                var full = false;
                var batch = await unitOfWork.ExecuteInTransactionAsync(async ct =>
                {
                    full = await PurgeBatchAsync(ct);
                    return Result.Success();
                }, CommitPolicy.OnSuccess, cancellationToken);
                if (batch.IsFailure || !full || clock.GetElapsedTime(started) >= MaxDuration) return batch;
            }
        });

    // One batch: the proposed ones nobody reviewed (F9) become discarded, with their own ninety days, and then the discarded and unclassified
    // ones that expired go. True when either list came out full, so there may be more.
    private async Task<bool> PurgeBatchAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var stale = await reader.ListStaleProposedIdsAsync(now, BatchSize, ct);
        foreach (var documentId in stale)
        {
            await repository.LockAsync(documentId, ct);
            if (await repository.GetAsync(documentId, ct) is { } document && document.IsStale(now)) document.Expire(now);
        }
        var expired = await reader.ListExpiredIdsAsync(now, BatchSize, ct);
        foreach (var documentId in expired)
        {
            await repository.LockAsync(documentId, ct);
            if (await repository.GetAsync(documentId, ct) is { DeleteAfterUtc: { } deleteAfter } document && deleteAfter <= now
                && document.State is FormReceivedDocumentState.Discarded or FormReceivedDocumentState.Unclassified)
                repository.Remove(document);
        }
        return stale.Count >= BatchSize || expired.Count >= BatchSize;
    }
}
