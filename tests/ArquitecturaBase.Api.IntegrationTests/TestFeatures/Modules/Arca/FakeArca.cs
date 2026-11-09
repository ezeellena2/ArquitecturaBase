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

/// <summary>Un punto de venta como lo devuelve <c>FEParamGetPtosVenta</c>: los textos tal cual, «S»/«N» y «NULL» o una fecha.</summary>
internal sealed record FakeArcaPointOfSale(int Number, string EmissionType, string Blocked, string DroppedOn);

/// <summary>Lo que devuelve <c>getPersona_v2</c> para la CUIT consultada.</summary>
internal sealed record FakeArcaPerson(string? BusinessName, string? FirstName, string? LastName, string? Street, string? Locality,
    string? Province, int[] TaxIds, bool Monotax, string[] ConstanciaErrors);

/// <summary>
/// Hace de ARCA sin red. Enruta por la operación del sobre SOAP, verifica la firma del pedido de ticket, cuenta las
/// llamadas por operación y servicio, guarda el último pedido de cada operación y deja programar respuestas, fallas o
/// retenerlas. Es uno por factory: cada test empieza con <see cref="Reset"/>.
/// </summary>
internal sealed class FakeArca(TimeProvider time)
{
    public const string InvoicingService = "wsfe";
    public const string RegistryService = "ws_sr_constancia_inscripcion";

    public static readonly FakeArcaPerson Company = new("Campo SA", null, null, "Ruta 9 km 400", "Marcos Juárez", "CORDOBA", [30], false, []);

    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Wsaa = "http://wsaa.view.sua.dvadac.desein.afip.gov";
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";
    private static readonly XNamespace Registry = "http://a5.soap.ws.server.puc.sr/";

    private readonly ConcurrentDictionary<string, int> calls = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, XElement> lastRequests = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, FakeArcaFailure> failures = new(StringComparer.Ordinal);
    private volatile TaskCompletionSource? gate;
    private int issued;

    /// <summary>Lo que responde <c>FEDummy</c>; «OK» es que el servidor anda.</summary>
    public (string App, string Db, string Auth) ServerStatus { get; set; } = ("OK", "OK", "OK");

    public IReadOnlyList<FakeArcaPointOfSale> PointsOfSale { get; set; } = [];

    /// <summary>Un error de WSFEv1 para <c>FEParamGetPtosVenta</c> (602 sin resultados, 600 token inválido).</summary>
    public int? PointsOfSaleError { get; set; }

    /// <summary>La persona del padrón; null responde la falla SOAP de una CUIT que no existe.</summary>
    public FakeArcaPerson? Person { get; set; } = Company;

    /// <summary>
    /// Las CUIT representadas que van en las relaciones del token, como un <c>&lt;sso&gt;</c> en base64; null emite el token
    /// corto de siempre, que no se lee.
    /// </summary>
    public IReadOnlyCollection<string>? Relations { get; set; }

    public int Calls(string operation, string service) => calls.GetValueOrDefault(operation + ":" + service);

    public XElement? LastRequest(string operation) => lastRequests.GetValueOrDefault(operation);

    public void Fail(string service, FakeArcaFailure failure) => failures[service] = failure;

    /// <summary>Retiene las respuestas hasta <see cref="Release"/>, después de contar la llamada.</summary>
    public void Hold() => gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release() => gate?.TrySetResult();

    public void Reset()
    {
        calls.Clear();
        lastRequests.Clear();
        failures.Clear();
        gate = null;
        ServerStatus = ("OK", "OK", "OK");
        PointsOfSale = [];
        PointsOfSaleError = null;
        Person = Company;
        Relations = null;
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var envelope = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
        var operation = envelope.Root?.Element(Soap + "Body")?.Elements().FirstOrDefault()
            ?? throw new InvalidOperationException("The ARCA test double received a request without a SOAP body.");
        lastRequests[operation.Name.LocalName] = operation;
        return operation.Name.LocalName switch
        {
            "loginCms" => await LoginAsync(operation, cancellationToken),
            "FEDummy" => await CountAndFailAsync("FEDummy", InvoicingService, cancellationToken) ?? Dummy(),
            "FEParamGetPtosVenta" => await CountAndFailAsync("FEParamGetPtosVenta", InvoicingService, cancellationToken) ?? PointsOfSaleResponse(),
            "getPersona_v2" => await CountAndFailAsync("getPersona_v2", RegistryService, cancellationToken) ?? PersonResponse(),
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
            new XElement("credentials", new XElement("token", Token(service, number)), new XElement("sign", $"sign-{service}-{number}")));
        return Envelope(HttpStatusCode.OK, new XElement(Wsaa + "loginCmsResponse", new XElement(Wsaa + "loginCmsReturn", ticket.ToString())));
    }

    // El token del WSAA es un <sso> en base64 con el login y las CUIT representadas (manual del WSAA, 6.3).
    private string Token(string service, int number) => Relations is not { } relations
        ? $"token-{service}-{number}"
        : Convert.ToBase64String(Encoding.UTF8.GetBytes(new XElement("sso", new XAttribute("version", "2.0"),
            new XElement("id", new XAttribute("unique_id", number)),
            new XElement("operation", new XAttribute("type", "login"), new XAttribute("value", "granted"),
                new XElement("login", new XAttribute("service", service),
                    new XElement("relations", relations.Select(cuit => new XElement("relation", new XAttribute("key", cuit), new XAttribute("reltype", 4)))))))
            .ToString(SaveOptions.DisableFormatting)));

    private HttpResponseMessage Dummy() => Envelope(HttpStatusCode.OK, new XElement(Fev1 + "FEDummyResponse",
        new XElement(Fev1 + "FEDummyResult",
            new XElement(Fev1 + "AppServer", ServerStatus.App),
            new XElement(Fev1 + "DbServer", ServerStatus.Db),
            new XElement(Fev1 + "AuthServer", ServerStatus.Auth))));

    private HttpResponseMessage PointsOfSaleResponse()
    {
        XElement result = PointsOfSaleError is { } code
            ? new XElement(Fev1 + "FEParamGetPtosVentaResult", new XElement(Fev1 + "Errors",
                new XElement(Fev1 + "Err", new XElement(Fev1 + "Code", code), new XElement(Fev1 + "Msg", "Error de prueba"))))
            : new XElement(Fev1 + "FEParamGetPtosVentaResult", new XElement(Fev1 + "ResultGet", PointsOfSale.Select(point =>
                new XElement(Fev1 + "PtoVenta",
                    new XElement(Fev1 + "Nro", point.Number),
                    new XElement(Fev1 + "EmisionTipo", point.EmissionType),
                    new XElement(Fev1 + "Bloqueado", point.Blocked),
                    new XElement(Fev1 + "FchBaja", point.DroppedOn)))));
        return Envelope(HttpStatusCode.OK, new XElement(Fev1 + "FEParamGetPtosVentaResponse", result));
    }

    // Los elementos de la respuesta del padrón van sin calificar, como dice su WSDL.
    private HttpResponseMessage PersonResponse()
    {
        if (Person is not { } person) return Fault("ns0:Server", "No existe persona con ese Id");
        var general = new XElement("datosGenerales",
            Optional("apellido", person.LastName),
            new XElement("domicilioFiscal",
                new XElement("codPostal", "2580"),
                Optional("descripcionProvincia", person.Province),
                Optional("direccion", person.Street),
                new XElement("idProvincia", 3),
                Optional("localidad", person.Locality)),
            Optional("nombre", person.FirstName),
            Optional("razonSocial", person.BusinessName),
            new XElement("tipoPersona", person.BusinessName is null ? "FISICA" : "JURIDICA"));
        var persona = new XElement("personaReturn", general,
            person.TaxIds.Length == 0 ? null : new XElement("datosRegimenGeneral", person.TaxIds.Select(id =>
                new XElement("impuesto", new XElement("estadoImpuesto", "AC"), new XElement("idImpuesto", id)))),
            person.Monotax ? new XElement("datosMonotributo", new XElement("categoriaMonotributo", "A"),
                new XElement("impuesto", new XElement("idImpuesto", 20))) : null,
            person.ConstanciaErrors.Length == 0 ? null : new XElement("errorConstancia", person.ConstanciaErrors.Select(error => new XElement("error", error))));
        return Envelope(HttpStatusCode.OK, new XElement(Registry + "getPersona_v2Response", persona));
    }

    private async Task<HttpResponseMessage?> CountAndFailAsync(string operation, string service, CancellationToken cancellationToken)
    {
        calls.AddOrUpdate(operation + ":" + service, 1, (_, count) => count + 1);
        if (gate is { } held) await held.Task.WaitAsync(cancellationToken);
        if (!failures.TryRemove(service, out var failure)) return null;
        switch (failure)
        {
            case FakeArcaFailure.AlreadyAuthenticated:
                return Fault("ns1:coe.alreadyAuthenticated", "El CEE ya posee un TA valido para el acceso al WSN solicitado");
            case FakeArcaFailure.NotAuthorized:
                return Fault("ns1:coe.notAuthorized", "Computador no autorizado a acceder al servicio");
            case FakeArcaFailure.ServiceUnavailable:
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            default:
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("A held ARCA call can only end by cancellation.");
        }
    }

    private static XElement? Optional(string name, string? value) => value is null ? null : new XElement(name, value);

    private static HttpResponseMessage Fault(string code, string message) => Envelope(HttpStatusCode.InternalServerError,
        new XElement(Soap + "Fault",
            new XElement("faultcode", new XAttribute(XNamespace.Xmlns + "ns1", "http://xml.apache.org/axis/"), code),
            new XElement("faultstring", message)));

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
