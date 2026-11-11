namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>The current code of catalog options by id, for an issuer that knows options by their code. Only reads.</summary>
public interface IFormCatalogItemCodeReader
{
    Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);

    /// <summary>The active option of a catalog with that code, for a registry that knows options by their code.</summary>
    Task<Guid?> FindActiveAsync(Guid catalogId, string code, CancellationToken cancellationToken);
}
