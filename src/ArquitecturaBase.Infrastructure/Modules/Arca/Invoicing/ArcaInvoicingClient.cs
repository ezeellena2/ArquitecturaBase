using System.Globalization;
using System.Xml.Linq;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Soap;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>
/// WSFEv1 con los nombres de su WSDL: todo en <c>http://ar.gov.afip.dif.FEV1/</c> y el <c>SOAPAction</c> con el nombre de
/// la operación. Los errores de negocio llegan en <c>Errors/Err</c>: el 602 es «sin resultados», y el 600 y el 601
/// rechazan el token o la CUIT.
/// </summary>
internal sealed class ArcaInvoicingClient(IHttpClientFactory httpClients, IOptions<ArcaOptions> options, TimeProvider timeProvider)
    : IArcaInvoicingClient
{
    private const string Service = "wsfe";
    private const int NoResults = 602;
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";
    private static readonly string[] ServerParts = ["AppServer", "DbServer", "AuthServer"];

    public async Task<ArcaServiceReply<bool>> CheckServerAsync(ArcaEnvironment environment, CancellationToken cancellationToken)
    {
        var result = await SendAsync(environment, new XElement(Fev1 + "FEDummy"), cancellationToken);
        if (result is not SoapResult.Success success) return Failure<bool>(result);
        var dummy = success.Content.Element(Fev1 + "FEDummyResult");
        return ArcaServiceReply.Ready(ServerParts.All(part => dummy?.Element(Fev1 + part)?.Value == "OK"));
    }

    public async Task<ArcaServiceReply<IReadOnlyList<ArcaPointOfSale>>> ListPointsOfSaleAsync(ArcaCertificateRef certificate,
        ArcaTicketOutcome.Ready ticket, CancellationToken cancellationToken)
    {
        var operation = new XElement(Fev1 + "FEParamGetPtosVenta", new XElement(Fev1 + "Auth",
            new XElement(Fev1 + "Token", ticket.Token), new XElement(Fev1 + "Sign", ticket.Sign), new XElement(Fev1 + "Cuit", certificate.Cuit)));
        var result = await SendAsync(certificate.Environment, operation, cancellationToken);
        if (result is not SoapResult.Success success) return Failure<IReadOnlyList<ArcaPointOfSale>>(result);

        var response = success.Content.Element(Fev1 + "FEParamGetPtosVentaResult");
        int[] errors = [.. response?.Element(Fev1 + "Errors")?.Elements(Fev1 + "Err").Select(error => Number(error.Element(Fev1 + "Code"))).OfType<int>() ?? []];
        if (errors.Contains(NoResults)) return ArcaServiceReply.Ready<IReadOnlyList<ArcaPointOfSale>>([]);
        if (errors.Any(code => code is 600 or 601)) return ArcaServiceReply.Failed<IReadOnlyList<ArcaPointOfSale>>(ArcaServiceCheckStatus.NotAuthorized);
        if (errors.Length > 0) return ArcaServiceReply.Failed<IReadOnlyList<ArcaPointOfSale>>(ArcaServiceCheckStatus.Failed, $"{Service}.{errors[0]}");

        return ArcaServiceReply.Ready<IReadOnlyList<ArcaPointOfSale>>(
            [.. response?.Element(Fev1 + "ResultGet")?.Elements(Fev1 + "PtoVenta").Select(PointOfSale).OfType<ArcaPointOfSale>() ?? []]);
    }

    private static ArcaPointOfSale? PointOfSale(XElement point) => Number(point.Element(Fev1 + "Nro")) is { } number
        ? new ArcaPointOfSale(number, point.Element(Fev1 + "EmisionTipo")?.Value.Trim() ?? string.Empty,
            string.Equals(point.Element(Fev1 + "Bloqueado")?.Value.Trim(), "S", StringComparison.Ordinal), DroppedOn(point.Element(Fev1 + "FchBaja")))
        : null;

    // «NULL» si no tiene baja; si no, la fecha como aaaammdd.
    private static DateOnly? DroppedOn(XElement? value) =>
        DateOnly.TryParseExact(value?.Value.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static int? Number(XElement? value) =>
        int.TryParse(value?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static ArcaServiceReply<T> Failure<T>(SoapResult result) => result is SoapResult.Fault
        ? ArcaServiceReply.Failed<T>(ArcaServiceCheckStatus.Failed, Service + ".fault")
        : ArcaServiceReply.Failed<T>(ArcaServiceCheckStatus.Unavailable);

    private Task<SoapResult> SendAsync(ArcaEnvironment environment, XElement operation, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var endpoint = (environment == ArcaEnvironment.Production ? settings.Production : settings.Testing).Services.GetValueOrDefault(Service)
            ?? throw new InvalidOperationException("Configure the ARCA address of the wsfe service.");
        return SoapEnvelope.SendAsync(httpClients.CreateClient(ArcaInfrastructureRegistration.InvoicingHttpClientName), endpoint,
            $"{Fev1.NamespaceName}{operation.Name.LocalName}", operation, settings, timeProvider, cancellationToken);
    }
}
