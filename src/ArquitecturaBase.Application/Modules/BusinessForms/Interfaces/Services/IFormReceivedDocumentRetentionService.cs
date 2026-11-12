using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// The daily purge of the inbox: the discarded and unclassified documents whose deletion date passed go with their reading and history,
/// and their copy stops being kept, so the cleanup of private files deletes it.
/// </summary>
public interface IFormReceivedDocumentRetentionService
{
    Task<Result> PurgeExpiredAsync(CancellationToken cancellationToken);
}
