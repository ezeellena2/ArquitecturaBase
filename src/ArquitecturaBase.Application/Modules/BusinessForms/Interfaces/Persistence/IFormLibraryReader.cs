namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>What the library reads (BI1-A): how many forms came from each standard and the active catalogs a standard names by Key.</summary>
public interface IFormLibraryReader
{
    Task<IReadOnlyDictionary<string, int>> ListCopyCountsAsync(IReadOnlyList<string> standardKeys, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, Guid>> ListCatalogIdsByKeyAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken);
}
