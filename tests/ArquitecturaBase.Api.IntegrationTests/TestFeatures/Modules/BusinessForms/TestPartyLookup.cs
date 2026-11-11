using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

/// <summary>
/// A registry that only exists to test «Completar con ARCA»: it completes the customers catalog of the test sale standard from its
/// «cuit» field, records what it is asked and answers what the test sets.
/// </summary>
internal sealed class TestPartyLookup : IStructuredPartyLookup
{
    private readonly List<string> _asked = [];

    public string CatalogKey => TestFormStandardSource.CustomersCatalog;
    public string TaxIdField => "cuit";
    public IReadOnlyDictionary<string, string> Name { get; } = new Dictionary<string, string> { ["es"] = "Padrón de prueba", ["en"] = "Test registry" };
    public StructuredPartyLookupResponse Next { get; set; } = new(null, null, "Arca.Registry.PersonNotFound");
    public IReadOnlyList<string> Asked { get { lock (_asked) return [.. _asked]; } }

    public Task<StructuredPartyLookupResponse> FindAsync(string taxId, CancellationToken cancellationToken)
    {
        lock (_asked) _asked.Add(taxId);
        return Task.FromResult(Next);
    }
}
