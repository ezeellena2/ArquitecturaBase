namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>What the library reads (BI1-A): how many forms came from each standard, the catalogs a standard names by Key and the
/// active options of a catalog with the codes a condition of the standard names.</summary>
public interface IFormLibraryReader
{
    Task<IReadOnlyDictionary<string, int>> ListCopyCountsAsync(IReadOnlyList<string> standardKeys, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, Models.FormLibraryCatalogRow>> ListCatalogsByKeyAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, Guid>> ListActiveItemIdsByCodeAsync(Guid catalogId, IReadOnlyList<string> codes, CancellationToken cancellationToken);
}
