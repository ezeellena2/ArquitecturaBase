using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Services.Registry;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Registry;

/// <summary>
/// «Completar con ARCA» (CA1): the constancia of a third party completes the «Clientes» of the «Factura» from its CUIT, with the
/// name (also whole in «Razón social»), the condition frente al IVA as the receiver code of WSFEv1 and the fiscal address. It
/// only reads, in the name of the signer: in delegation the platform consults with its own CUIT, without the company delegating
/// the constancia. Nothing is saved.
/// </summary>
internal sealed class ArcaStructuredPartyLookup(ArcaServiceSessionIssuer access, IArcaRegistryClient client) : IStructuredPartyLookup
{
    private static readonly ArcaServiceCode Constancia = ArcaServiceCode.Create("ws_sr_constancia_inscripcion")!.Value;

    public string CatalogKey => ArcaCustomersCatalog.Key;
    public string TaxIdField => ArcaCustomersCatalog.TaxId;
    public IReadOnlyDictionary<string, string> Name { get; } = new Dictionary<string, string> { ["es"] = "ARCA", ["en"] = "ARCA" };

    public async Task<StructuredPartyLookupResponse> FindAsync(string taxId, CancellationToken cancellationToken)
    {
        if (CuitNumber.Normalize(taxId) is not { } cuit) return new(null, null, ArcaRegistryErrors.CuitInvalid.Code);
        var session = await access.OpenAsSignerAsync(Constancia, cancellationToken);
        if (session.IsFailure) return new(null, null, session.Error.Code);
        var reply = await client.FindPersonAsync(session.Value.Certificate, session.Value.Ticket, cuit, cancellationToken);
        // The registry answers a CUIT it does not know with a fault.
        if (reply.FailureCode == "registry.fault") return new(null, null, ArcaRegistryErrors.PersonNotFound.Code);
        if (reply is not { Status: ArcaServiceCheckStatus.Ready, Value: { } person })
            return new(null, null, ArcaServiceSessionIssuer.Failure(reply.Status).Code);
        if (!ArcaCompanyDataPolicy.HasData(person)) return new(null, null, ArcaRegistryErrors.PersonNotFound.Code);
        var legalName = ArcaCompanyDataPolicy.LegalName(person);
        // The whole name also goes in its field: the text of an option of «Clientes» admits 30 characters.
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ArcaCustomersCatalog.TaxId] = cuit,
            [ArcaCustomersCatalog.LegalName] = legalName,
            [ArcaCustomersCatalog.VatCondition] = ArcaCompanyDataPolicy.ReceiverVatCondition(person).ToString(CultureInfo.InvariantCulture),
        };
        if (ArcaCompanyDataPolicy.FiscalAddress(person) is { Length: > 0 } address) fields[ArcaCustomersCatalog.Address] = address;
        return new(legalName, fields);
    }
}
