using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// «Completar con ARCA» (CA1): the registry completes the «Clientes» of the «Factura» from a CUIT, with the name (also whole in
/// «Razón social»), the condition frente al IVA as the receiver code of WSFEv1 and the fiscal address. The registry family answers
/// through <see cref="IArcaPartySource"/>, in the name of the signer. Nothing is saved.
/// </summary>
internal sealed class ArcaStructuredPartyLookup(IArcaPartySource parties) : IStructuredPartyLookup
{
    public string CatalogKey => ArcaCustomersCatalog.Key;
    public string TaxIdField => ArcaCustomersCatalog.TaxId;
    public IReadOnlyDictionary<string, string> Name { get; } = new Dictionary<string, string> { ["es"] = "ARCA", ["en"] = "ARCA" };

    public async Task<StructuredPartyLookupResponse> FindAsync(string taxId, CancellationToken cancellationToken)
    {
        var party = await parties.FindAsync(taxId, cancellationToken);
        return party.IsSuccess ? new(party.Value.LegalName, Fields(party.Value)) : new(null, null, party.Error.Code);
    }

    /// <summary>
    /// A party in the fields of «Clientes», by their keys. The whole name also goes in its field: the text of an option admits 30
    /// characters.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Fields(ArcaParty party)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ArcaCustomersCatalog.TaxId] = party.Cuit,
            [ArcaCustomersCatalog.LegalName] = party.LegalName,
            [ArcaCustomersCatalog.VatCondition] = party.VatConditionId.ToString(CultureInfo.InvariantCulture),
        };
        if (party.FiscalAddress is { } address) fields[ArcaCustomersCatalog.Address] = address;
        return fields;
    }
}
