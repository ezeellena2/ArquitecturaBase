using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>How the fed catalogs stand, to know which ones are due. Only reads.</summary>
public interface IFormCatalogFeedReader
{
    Task<IReadOnlyList<FormCatalogFeedRow>> ListAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);
}
