using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// The lists an installed module keeps for the forms catalogs, such as the tables of a tax agency. Forms keeps a read-only local
/// copy of each (an External catalog) in the system folder the feed names, and asks again once a day: reading has no effect
/// outside the system. An optional collection: without feeds there are no External catalogs. Keys are stable and unique.
/// </summary>
public interface IStructuredCatalogFeed
{
    IReadOnlyList<StructuredFeedCatalog> Catalogs { get; }

    /// <summary>The current items of one of its catalogs, or a stable failure code when it cannot read them now.</summary>
    Task<StructuredFeedReadResponse> ReadAsync(string catalogKey, CancellationToken cancellationToken);
}
