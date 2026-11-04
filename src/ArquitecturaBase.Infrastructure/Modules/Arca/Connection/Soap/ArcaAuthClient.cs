using System.Globalization;
using System.Xml.Linq;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Soap;

/// <summary>
/// <c>LoginCms</c> del WSAA: firma el pedido de ticket y traduce la respuesta. Las fallas conocidas se distinguen por su
/// código; <c>coe.alreadyAuthenticated</c> bloquea el servicio por la ventana del ambiente.
/// </summary>
internal sealed class ArcaAuthClient(IHttpClientFactory httpClients, IArcaCredentialService credentials, IOptions<ArcaOptions> options,
    TimeProvider timeProvider)
{
    private static readonly XNamespace Wsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";

    // xml.generationTime.invalid, xml.expirationTime.invalid o un texto que habla de esa ventana.
    private static readonly string[] ClockWords = ["generationTime", "expirationTime", "expiración"];

    public async Task<ArcaTicketOutcome> LoginAsync(ArcaCertificateRef certificate, ArcaServiceCode service, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var environment = certificate.Environment == ArcaEnvironment.Production ? settings.Production : settings.Testing;
        var request = ArcaLoginTicketRequest.Build(service.Value, timeProvider.GetUtcNow());
        var cms = credentials.SignLoginRequest(certificate.ConnectionId, certificate.Id, certificate.ProtectedPrivateKey, certificate.CertificatePem, request);
        var result = await SoapEnvelope.SendAsync(httpClients.CreateClient(ArcaInfrastructureRegistration.WsaaHttpClientName), environment.Wsaa!,
            soapAction: string.Empty, new XElement(Wsaa + "loginCms", new XElement(Wsaa + "in0", cms)), settings, timeProvider, cancellationToken);
        return result switch
        {
            SoapResult.Success success => ReadTicket(success.Content),
            SoapResult.Fault fault => FromFault(fault, environment),
            _ => new ArcaTicketOutcome.Unavailable(),
        };
    }

    // loginCmsReturn trae el loginTicketResponse como texto escapado.
    private static ArcaTicketOutcome ReadTicket(XElement content)
    {
        var ticket = SoapEnvelope.Parse(content.Elements().FirstOrDefault(element => element.Name.LocalName == "loginCmsReturn")?.Value)?.Root;
        var token = ticket?.Element("credentials")?.Element("token")?.Value;
        var sign = ticket?.Element("credentials")?.Element("sign")?.Value;
        var expiration = ticket?.Element("header")?.Element("expirationTime")?.Value;
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sign)
            || !DateTimeOffset.TryParse(expiration, CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresAt))
        {
            return new ArcaTicketOutcome.Unavailable();
        }

        return new ArcaTicketOutcome.Ready(token, sign, expiresAt.UtcDateTime);
    }

    private ArcaTicketOutcome FromFault(SoapResult.Fault fault, ArcaEnvironmentOptions environment) => fault.Code switch
    {
        "coe.alreadyAuthenticated" => new ArcaTicketOutcome.Blocked(timeProvider.GetUtcNow().UtcDateTime + environment.TicketRetryWindow),
        "coe.notAuthorized" or "cms.cert.untrusted" => new ArcaTicketOutcome.NotAuthorized(),
        _ when IsAboutTheClock(fault) => new ArcaTicketOutcome.ClockSkew(),
        _ => new ArcaTicketOutcome.Unavailable(),
    };

    private static bool IsAboutTheClock(SoapResult.Fault fault) =>
        ClockWords.Any(word =>
            fault.Code.Contains(word, StringComparison.OrdinalIgnoreCase) || fault.Message.Contains(word, StringComparison.OrdinalIgnoreCase));
}
