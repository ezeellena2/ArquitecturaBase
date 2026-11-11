namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// Catalog options and their codes, for whoever knows options by their code (an issuer, a registry, a library standard). Only reads,
/// in one query per call and projecting the two columns.
/// </summary>
public interface IFormCatalogItemCodeReader
{
    /// <summary>The current code of each option by id.</summary>
    Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);

    /// <summary>The active options of a catalog with these codes, by code; a code it lacks is not in the result.</summary>
    Task<IReadOnlyDictionary<string, Guid>> ListActiveAsync(Guid catalogId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken);
}
