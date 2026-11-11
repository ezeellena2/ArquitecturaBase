namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>What the library reads (BI1-A): how many forms came from each standard, the catalogs a standard names by Key and the
/// prefixes in use. The options a standard names by code are read by <see cref="IFormCatalogItemCodeReader"/>.</summary>
public interface IFormLibraryReader
{
    Task<IReadOnlyDictionary<string, int>> ListCopyCountsAsync(IReadOnlyList<string> standardKeys, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, Models.FormLibraryCatalogRow>> ListCatalogsByKeyAsync(IReadOnlyList<string> catalogKeys, CancellationToken cancellationToken);
    /// <summary>The prefixes of every form, deleted ones too, that start with one of these stems (letters and digits), in one query.</summary>
    Task<IReadOnlySet<string>> ListPrefixesAsync(IReadOnlyList<string> stems, CancellationToken cancellationToken);
}
