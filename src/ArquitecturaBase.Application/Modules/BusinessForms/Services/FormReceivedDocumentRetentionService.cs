using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The inbox's retention (decision 9): a batch of the discarded and unclassified documents whose deletion date passed, each read again
/// under its lock (one reopened meanwhile stays), deleted with its reading, proposal and history; its copy is no longer kept by the
/// retention source, so the cleanup of private files deletes it. A confirmed one follows its response and never expires here.
/// </summary>
internal sealed class FormReceivedDocumentRetentionService(IFormReceivedDocumentReader reader, IFormReceivedDocumentRepository repository,
    IUnitOfWork unitOfWork, TimeProvider clock, ILogger<FormReceivedDocumentRetentionService> logger) : IFormReceivedDocumentRetentionService
{
    public const int BatchSize = 100;

    public Task<Result> PurgeExpiredAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "PurgeExpiredFormReceivedDocuments", () => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var documentId in await reader.ListExpiredIdsAsync(now, BatchSize, ct))
            {
                await repository.LockAsync(documentId, ct);
                if (await repository.GetAsync(documentId, ct) is { DeleteAfterUtc: { } deleteAfter } document && deleteAfter <= now
                    && document.State is FormReceivedDocumentState.Discarded or FormReceivedDocumentState.Unclassified)
                    repository.Remove(document);
            }
            return Result.Success();
        }, CommitPolicy.OnSuccess, cancellationToken));
}
