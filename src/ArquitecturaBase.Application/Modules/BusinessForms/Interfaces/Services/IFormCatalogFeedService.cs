using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// The External catalogs the installed feeds keep: which ones are due and their refresh in three steps the worker runs in order,
/// each in its own scope: reserve, read the source outside any transaction, and apply the differences with the catalog revision
/// (creating the catalog, in its system folder, the first time its source answered). It has no route.
/// </summary>
public interface IFormCatalogFeedService
{
    /// <summary>The keys of the fed catalogs never read, read more than a day ago, or failed more than 15 minutes ago.</summary>
    Task<IReadOnlyList<string>> ListDueAsync(CancellationToken cancellationToken);

    /// <summary>Takes the short reservation of one; false when another host holds it. One not created yet is read without one.</summary>
    Task<Result<bool>> ReserveAsync(string catalogKey, CancellationToken cancellationToken);

    /// <summary>Reads its source, outside any transaction; a source that throws reads as unavailable.</summary>
    Task<StructuredFeedReadResponse> ReadAsync(string catalogKey, CancellationToken cancellationToken);

    /// <summary>Creates the catalog the first time its source answered, and applies what it read or records why it could not.</summary>
    Task<Result> ApplyAsync(string catalogKey, StructuredFeedReadResponse read, CancellationToken cancellationToken);
}
