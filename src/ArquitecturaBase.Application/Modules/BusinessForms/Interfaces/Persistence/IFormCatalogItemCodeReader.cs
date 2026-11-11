namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>The current code of catalog options by id, for an issuer that knows options by their code. Only reads.</summary>
public interface IFormCatalogItemCodeReader
{
    Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
}
