using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Interfaces.Channels;

/// <summary>
/// A registry a module brings to complete a party from its tax id («Completar con ARCA»). It says which catalog it completes, by
/// the stable Key a standard gave it, and which of its fields holds the tax id; given one, it answers the name and the values of
/// the other fields by their keys (a field that names an option of another catalog, by the code of that option). An optional
/// collection without an off adapter: without lookups, no catalog offers it. It only reads, outside any UoW. The quotas of forms do
/// not protect the registry: the module limits and caches its own calls.
/// </summary>
public interface IStructuredPartyLookup
{
    string CatalogKey { get; }

    string TaxIdField { get; }

    /// <summary>The name of the registry by language, for the button («Completar con ARCA»).</summary>
    IReadOnlyDictionary<string, string> Name { get; }

    Task<StructuredPartyLookupResponse> FindAsync(string taxId, CancellationToken cancellationToken);
}
