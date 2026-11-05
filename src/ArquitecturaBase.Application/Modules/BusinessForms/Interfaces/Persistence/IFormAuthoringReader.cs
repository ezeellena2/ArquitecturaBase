using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// Existing forms and catalogs for the authoring assistant: names and fields only, newest first. Creating from a proposal
/// also asks what that proposal already created, deleted or discarded included, and whether a name is in use.
/// </summary>
public interface IFormAuthoringReader
{
    Task<IReadOnlyList<FormAuthoringDefinitionStoredRow>> ListDefinitionsAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<FormAuthoringCreatedRow?> FindDefinitionByProposalAsync(Guid proposalId, CancellationToken cancellationToken);
    Task<FormAuthoringNamedRow?> FindDefinitionByPrefixAsync(string prefix, CancellationToken cancellationToken);
    Task<FormAuthoringCreatedRow?> FindCatalogByProposalAsync(Guid proposalId, CancellationToken cancellationToken);
    Task<bool> ExistsDefinitionNameAsync(string culture, string name, CancellationToken cancellationToken);
    Task<bool> ExistsCatalogNameAsync(string culture, string name, CancellationToken cancellationToken);
}
