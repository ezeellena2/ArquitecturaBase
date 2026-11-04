using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>Existing forms and catalogs for the authoring assistant: names and fields only, newest first.</summary>
public interface IFormAuthoringReader
{
    Task<IReadOnlyList<FormAuthoringDefinitionStoredRow>> ListDefinitionsAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(int take, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
