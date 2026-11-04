using System.Globalization;
using System.Xml.Linq;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Soap;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Registry;

/// <summary>
/// <c>getPersona_v2</c> de la constancia de inscripción con los nombres de su WSDL: la operación en
/// <c>http://a5.soap.ws.server.puc.sr/</c>, los elementos de entrada y de la respuesta sin calificar y el
/// <c>SOAPAction</c> vacío. Los errores de la constancia vienen en la respuesta y se devuelven con la persona.
/// </summary>
internal sealed class ArcaRegistryClient(IHttpClientFactory httpClients, IOptions<ArcaOptions> options, TimeProvider timeProvider)
    : IArcaRegistryClient
{
    private const string Service = "ws_sr_constancia_inscripcion";
    private static readonly XNamespace Registry = "http://a5.soap.ws.server.puc.sr/";
    private static readonly string[] ErrorGroups = ["errorConstancia", "errorRegimenGeneral", "errorMonotributo"];

    public async Task<ArcaServiceReply<ArcaRegistryPerson>> FindPersonAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        string cuit, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var endpoint = (certificate.Environment == ArcaEnvironment.Production ? settings.Production : settings.Testing).Services.GetValueOrDefault(Service)
            ?? throw new InvalidOperationException("Configure the ARCA address of the ws_sr_constancia_inscripcion service.");
        var operation = new XElement(Registry + "getPersona_v2", new XAttribute(XNamespace.Xmlns + "a5", Registry),
            new XElement("token", ticket.Token), new XElement("sign", ticket.Sign),
            new XElement("cuitRepresentada", certificate.Cuit), new XElement("idPersona", cuit));
        var result = await SoapEnvelope.SendAsync(httpClients.CreateClient(ArcaInfrastructureRegistration.RegistryHttpClientName), endpoint,
            soapAction: string.Empty, operation, settings, timeProvider, cancellationToken);
        return result switch
        {
            SoapResult.Success { Content: var content } when content.Element("personaReturn") is { } persona => ArcaServiceReply.Ready(Person(persona)),
            SoapResult.Fault => ArcaServiceReply.Failed<ArcaRegistryPerson>(ArcaServiceCheckStatus.Failed, "registry.fault"),
            _ => ArcaServiceReply.Failed<ArcaRegistryPerson>(ArcaServiceCheckStatus.Unavailable),
        };
    }

    private static ArcaRegistryPerson Person(XElement persona)
    {
        var general = persona.Element("datosGenerales");
        var address = general?.Element("domicilioFiscal");
        return new ArcaRegistryPerson(
            Text(general, "razonSocial"), Text(general, "nombre"), Text(general, "apellido"),
            address is null ? null : new ArcaRegistryAddress(Text(address, "direccion"), Text(address, "localidad"),
                Text(address, "descripcionProvincia"), Text(address, "codPostal")),
            [.. persona.Element("datosRegimenGeneral")?.Elements("impuesto").Select(tax => TaxId(Text(tax, "idImpuesto"))).OfType<int>() ?? []],
            persona.Element("datosMonotributo") is not null,
            [.. ErrorGroups.SelectMany(group => persona.Element(group)?.Elements("error") ?? []).Select(error => error.Value.Trim())]);
    }

    private static string? Text(XElement? parent, string name) => parent?.Element(name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static int? TaxId(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
}
