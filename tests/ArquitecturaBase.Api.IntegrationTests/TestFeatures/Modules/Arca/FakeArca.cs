using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Xml.Linq;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>Una falla programada para la próxima llamada de un servicio.</summary>
internal enum FakeArcaFailure
{
    AlreadyAuthenticated,
    NotAuthorized,
    ServiceUnavailable,
    Hang,
}

/// <summary>
/// Hace de ARCA sin red. Enruta por la operación del sobre SOAP, verifica la firma del pedido de ticket, cuenta las
/// llamadas por operación y servicio, y deja programar fallas o retener las respuestas. Es uno por factory: cada test
/// empieza con <see cref="Reset"/>.
/// </summary>
internal sealed class FakeArca(TimeProvider time)
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Wsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";

    private readonly ConcurrentDictionary<string, int> calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FakeArcaFailure> failures = new(StringComparer.Ordinal);
    private volatile TaskCompletionSource? gate;
    private int issued;

    public int Calls(string operation, string service) => calls.GetValueOrDefault(operation + ":" + service);

    public void Fail(string service, FakeArcaFailure failure) => failures[service] = failure;

    /// <summary>Retiene las respuestas hasta <see cref="Release"/>, después de contar la llamada.</summary>
    public void Hold() => gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => gate?.TrySetResult();

    public void Reset()
    {
        calls.Clear();
        failures.Clear();
        gate = null;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var envelope = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var operation = envelope.Root?.Element(Soap + "Body")?.Elements().FirstOrDefault()
            ?? throw new InvalidOperationException("The ARCA test double received a request without a SOAP body.");
        return operation.Name.LocalName switch
        {
            "loginCms" => await LoginAsync(operation, cancellationToken),
            _ => throw new InvalidOperationException($"The ARCA test double has no operation {operation.Name.LocalName}."),
        };
    }

    private async Task<HttpResponseMessage> LoginAsync(XElement operation, CancellationToken cancellationToken)
    {
        var cms = new SignedCms();
        cms.Decode(Convert.FromBase64String(operation.Element(Wsaa + "in0")!.Value));
        cms.CheckSignature(verifySignatureOnly: true);
        var service = XDocument.Parse(Encoding.UTF8.GetString(cms.ContentInfo.Content)).Root!.Element("service")!.Value;

        if (await CountAndFailAsync("loginCms", service, cancellationToken) is { } failure) return failure;

        var number = Interlocked.Increment(ref issued);
        var expires = time.GetUtcNow().AddHours(12).ToOffset(TimeSpan.FromHours(-3))
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);
        var ticket = new XElement("loginTicketResponse", new XAttribute("version", "1.0"),
            new XElement("header", new XElement("expirationTime", expires)),
            new XElement("credentials", new XElement("token", $"token-{service}-{number}"), new XElement("sign", $"sign-{service}-{number}")));
        return Envelope(HttpStatusCode.OK, new XElement(Wsaa + "loginCmsResponse", new XElement(Wsaa + "loginCmsReturn", ticket.ToString())));
    }

    private async Task<HttpResponseMessage?> CountAndFailAsync(string operation, string service, CancellationToken cancellationToken)
    {
        calls.AddOrUpdate(operation + ":" + service, 1, (_, count) => count + 1);
        if (gate is { } held) await held.Task.WaitAsync(cancellationToken);
        if (!failures.TryRemove(service, out var failure)) return null;
        switch (failure)
        {
            case FakeArcaFailure.AlreadyAuthenticated:
                return Fault("coe.alreadyAuthenticated", "El CEE ya posee un TA valido para el acceso al WSN solicitado");
            case FakeArcaFailure.NotAuthorized:
                return Fault("coe.notAuthorized", "Computador no autorizado a acceder al servicio");
            case FakeArcaFailure.ServiceUnavailable:
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            default:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("A held ARCA call can only end by cancellation.");
        }
    }

    private static HttpResponseMessage Fault(string code, string message)
    {
        XNamespace axis = "http://xml.apache.org/axis/";
        return Envelope(HttpStatusCode.InternalServerError, new XElement(Soap + "Fault",
            new XElement("faultcode", new XAttribute(XNamespace.Xmlns + "ns1", axis), "ns1:" + code),
            new XElement("faultstring", message)));
    }

    private static HttpResponseMessage Envelope(HttpStatusCode status, XElement content)
    {
        var document = new XElement(Soap + "Envelope", new XAttribute(XNamespace.Xmlns + "soapenv", Soap), new XElement(Soap + "Body", content));
        return new HttpResponseMessage(status) { Content = new StringContent(document.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml") };
    }
}

/// <summary>El handler de cada cliente HTTP de ARCA en los tests; la factoría lo descarta, el estado queda en <see cref="FakeArca"/>.</summary>
internal sealed class FakeArcaHandler(FakeArca arca) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        arca.SendAsync(request, cancellationToken);
}
