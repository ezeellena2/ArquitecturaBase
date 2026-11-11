using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>A fed catalog by the stable key of its source, tracked to apply what the source sent. The caller holds the lock of the key.</summary>
public interface IFormCatalogFeedRepository
{
    Task<BusinessFormCatalog?> GetByKeyAsync(string key, CancellationToken cancellationToken);
}
