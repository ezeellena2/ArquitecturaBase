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
/// rechazan el token o la CUIT. Los elementos del pedido siguen el orden del manual (4.7), que el esquema exige.
/// </summary>
internal sealed class ArcaInvoicingClient(IHttpClientFactory httpClients, IOptions<ArcaOptions> options, TimeProvider timeProvider)
    : IArcaInvoicingClient
{
    private const string Service = "wsfe";
    private const int NoResults = 602;
    private const int NotNext = 10016;
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";
    private static readonly string[] ServerParts = ["AppServer", "DbServer", "AuthServer"];
    private static readonly int[] InternalErrors = [500, 501, 502];

    private static readonly Dictionary<ArcaParameterTable, (string Operation, string Row)> Tables = new()
    {
        [ArcaParameterTable.ReceiverVatConditions] = ("FEParamGetCondicionIvaReceptor", "CondicionIvaReceptor"),
        [ArcaParameterTable.DocumentTypes] = ("FEParamGetTiposDoc", "DocTipo"),
        [ArcaParameterTable.Concepts] = ("FEParamGetTiposConcepto", "ConceptoTipo"),
        [ArcaParameterTable.Currencies] = ("FEParamGetTiposMonedas", "Moneda"),
        [ArcaParameterTable.VatRates] = ("FEParamGetTiposIva", "IvaTipo"),
    };

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
        var result = await SendAsync(certificate.Environment, new XElement(Fev1 + "FEParamGetPtosVenta", Auth(certificate, ticket)), cancellationToken);
        if (result is not SoapResult.Success success) return Failure<IReadOnlyList<ArcaPointOfSale>>(result);

        var response = success.Content.Element(Fev1 + "FEParamGetPtosVentaResult");
        if (ErrorReply<IReadOnlyList<ArcaPointOfSale>>(response, []) is { } error) return error;
        return ArcaServiceReply.Ready<IReadOnlyList<ArcaPointOfSale>>(
            [.. response?.Element(Fev1 + "ResultGet")?.Elements(Fev1 + "PtoVenta").Select(PointOfSale).OfType<ArcaPointOfSale>() ?? []]);
    }

    public async Task<ArcaServiceReply<long>> GetLastAuthorizedAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket, int pointOfSale,
        int voucherType, CancellationToken cancellationToken)
    {
        var operation = new XElement(Fev1 + "FECompUltimoAutorizado", Auth(certificate, ticket),
            new XElement(Fev1 + "PtoVta", pointOfSale), new XElement(Fev1 + "CbteTipo", voucherType));
        var result = await SendAsync(certificate.Environment, operation, cancellationToken);
        if (result is not SoapResult.Success success) return Failure<long>(result);

        var response = success.Content.Element(Fev1 + "FECompUltimoAutorizadoResult");
        if (ErrorReply(response, 0L) is { } error) return error;
        return LongValue(response?.Element(Fev1 + "CbteNro")) is { } number
            ? ArcaServiceReply.Ready(number)
            : ArcaServiceReply.Failed<long>(ArcaServiceCheckStatus.Unavailable);
    }

    public async Task<ArcaServiceReply<ArcaAuthorization>> RequestAuthorizationAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        ArcaVoucherRequest voucher, CancellationToken cancellationToken)
    {
        var operation = new XElement(Fev1 + "FECAESolicitar", Auth(certificate, ticket),
            new XElement(Fev1 + "FeCAEReq",
                new XElement(Fev1 + "FeCabReq",
                    new XElement(Fev1 + "CantReg", 1), new XElement(Fev1 + "PtoVta", voucher.PointOfSale), new XElement(Fev1 + "CbteTipo", voucher.VoucherType)),
                new XElement(Fev1 + "FeDetReq", Detail(voucher))));
        var result = await SendAsync(certificate.Environment, operation, cancellationToken);
        return result switch
        {
            SoapResult.Success success => ArcaServiceReply.Ready(Authorization(success.Content.Element(Fev1 + "FECAESolicitarResult"))),
            // Una falla SOAP no dice si ARCA alcanzó a procesar el pedido.
            SoapResult.Fault => ArcaServiceReply.Ready(new ArcaAuthorization(ArcaAuthorizationResult.Uncertain, null, null, [], [])),
            _ => ArcaServiceReply.Failed<ArcaAuthorization>(ArcaServiceCheckStatus.Unavailable),
        };
    }

    public async Task<ArcaServiceReply<ArcaIssuedVoucher?>> FindVoucherAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        int pointOfSale, int voucherType, long number, CancellationToken cancellationToken)
    {
        var operation = new XElement(Fev1 + "FECompConsultar", Auth(certificate, ticket),
            new XElement(Fev1 + "FeCompConsReq",
                new XElement(Fev1 + "CbteTipo", voucherType), new XElement(Fev1 + "CbteNro", number), new XElement(Fev1 + "PtoVta", pointOfSale)));
        var result = await SendAsync(certificate.Environment, operation, cancellationToken);
        if (result is not SoapResult.Success success) return Failure<ArcaIssuedVoucher?>(result);

        var response = success.Content.Element(Fev1 + "FECompConsultarResult");
        if (ErrorReply<ArcaIssuedVoucher?>(response, null) is { } error) return error;
        var found = response?.Element(Fev1 + "ResultGet");
        if (found is null) return ArcaServiceReply.Failed<ArcaIssuedVoucher?>(ArcaServiceCheckStatus.Unavailable);
        if (Number(found.Element(Fev1 + "Concepto")) is not { } concept || Number(found.Element(Fev1 + "DocTipo")) is not { } documentType
            || LongValue(found.Element(Fev1 + "DocNro")) is not { } documentNumber || Date(found.Element(Fev1 + "CbteFch")) is not { } issuedOn
            || Amount(found.Element(Fev1 + "ImpTotal")) is not { } total)
            return ArcaServiceReply.Failed<ArcaIssuedVoucher?>(ArcaServiceCheckStatus.Unavailable);

        return ArcaServiceReply.Ready<ArcaIssuedVoucher?>(new ArcaIssuedVoucher(concept, documentType, documentNumber, issuedOn, total,
            Text(found.Element(Fev1 + "Resultado")), Text(found.Element(Fev1 + "CodAutorizacion")), Date(found.Element(Fev1 + "FchVto"))));
    }

    public async Task<ArcaServiceReply<IReadOnlyList<ArcaParameter>>> ListParametersAsync(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket,
        ArcaParameterTable table, CancellationToken cancellationToken)
    {
        var (name, row) = Tables[table];
        var result = await SendAsync(certificate.Environment, new XElement(Fev1 + name, Auth(certificate, ticket)), cancellationToken);
        if (result is not SoapResult.Success success) return Failure<IReadOnlyList<ArcaParameter>>(result);

        var response = success.Content.Element(Fev1 + name + "Result");
        if (ErrorReply<IReadOnlyList<ArcaParameter>>(response, []) is { } error) return error;
        return ArcaServiceReply.Ready<IReadOnlyList<ArcaParameter>>([.. response?.Element(Fev1 + "ResultGet")?.Elements(Fev1 + row)
            .Select(Parameter).OfType<ArcaParameter>() ?? []]);
    }

    private static ArcaParameter? Parameter(XElement row) => Text(row.Element(Fev1 + "Id")) is { } id
        ? new ArcaParameter(id, Text(row.Element(Fev1 + "Desc")) ?? string.Empty, Date(row.Element(Fev1 + "FchDesde")), Date(row.Element(Fev1 + "FchHasta")),
            Text(row.Element(Fev1 + "Cmp_Clase")))
        : null;

    private static XElement Detail(ArcaVoucherRequest voucher)
    {
        var amounts = voucher.Amounts;
        return new XElement(Fev1 + "FECAEDetRequest",
            new XElement(Fev1 + "Concepto", voucher.Concept),
            new XElement(Fev1 + "DocTipo", voucher.DocumentType),
            new XElement(Fev1 + "DocNro", voucher.DocumentNumber),
            new XElement(Fev1 + "CbteDesde", voucher.Number),
            new XElement(Fev1 + "CbteHasta", voucher.Number),
            new XElement(Fev1 + "CbteFch", Format(voucher.IssuedOn)),
            new XElement(Fev1 + "ImpTotal", Format(amounts.Total)),
            new XElement(Fev1 + "ImpTotConc", Format(amounts.NotTaxed)),
            new XElement(Fev1 + "ImpNeto", Format(amounts.Net)),
            new XElement(Fev1 + "ImpOpEx", Format(amounts.Exempt)),
            new XElement(Fev1 + "ImpTrib", Format(amounts.Taxes)),
            new XElement(Fev1 + "ImpIVA", Format(amounts.Vat)),
            Optional("FchServDesde", voucher.ServiceFrom),
            Optional("FchServHasta", voucher.ServiceTo),
            Optional("FchVtoPago", voucher.PaymentDueOn),
            new XElement(Fev1 + "MonId", voucher.CurrencyId),
            new XElement(Fev1 + "MonCotiz", voucher.CurrencyRate.ToString("0.######", CultureInfo.InvariantCulture)),
            new XElement(Fev1 + "CondicionIVAReceptorId", voucher.ReceiverVatConditionId),
            voucher.Associated.Count == 0 ? null : new XElement(Fev1 + "CbtesAsoc", voucher.Associated.Select(associated =>
                new XElement(Fev1 + "CbteAsoc",
                    new XElement(Fev1 + "Tipo", associated.VoucherType),
                    new XElement(Fev1 + "PtoVta", associated.PointOfSale),
                    new XElement(Fev1 + "Nro", associated.Number),
                    associated.Cuit is null ? null : new XElement(Fev1 + "Cuit", associated.Cuit),
                    associated.IssuedOn is { } date ? new XElement(Fev1 + "CbteFch", Format(date)) : null))),
            amounts.VatRates.Count == 0 ? null : new XElement(Fev1 + "Iva", amounts.VatRates.Select(rate =>
                new XElement(Fev1 + "AlicIva",
                    new XElement(Fev1 + "Id", rate.RateId), new XElement(Fev1 + "BaseImp", Format(rate.Base)), new XElement(Fev1 + "Importe", Format(rate.Amount))))));
    }

    /// <summary>
    /// Aprobado sólo con resultado A y CAE. Un 10016, un error interno o una respuesta sin detalle no dicen si se emitió: son
    /// inciertos. El resto, con resultado R o errores de validación, es un rechazo.
    /// </summary>
    private static ArcaAuthorization Authorization(XElement? result)
    {
        var errors = Messages(result?.Element(Fev1 + "Errors")?.Elements(Fev1 + "Err"));
        var detail = result?.Element(Fev1 + "FeDetResp")?.Elements().FirstOrDefault();
        var observations = Messages(detail?.Element(Fev1 + "Observaciones")?.Elements(Fev1 + "Obs"));
        var outcome = Text(detail?.Element(Fev1 + "Resultado"));
        var cae = Text(detail?.Element(Fev1 + "CAE"));

        if (outcome == "A" && cae is not null)
            return new ArcaAuthorization(ArcaAuthorizationResult.Approved, cae, Date(detail?.Element(Fev1 + "CAEFchVto")), observations, errors);

        var uncertain = errors.Any(error => InternalErrors.Contains(error.Code) || error.Code == NotNext)
            || observations.Any(observation => observation.Code == NotNext)
            || (outcome != "R" && errors.Count == 0);
        return new ArcaAuthorization(uncertain ? ArcaAuthorizationResult.Uncertain : ArcaAuthorizationResult.Rejected, null, null, observations, errors);
    }

    /// <summary>
    /// Los errores de WSFEv1 que no son un dato: 602 es «sin resultados» (vuelve <paramref name="empty"/>, o nada si es null),
    /// 600 y 601 rechazan el token o la CUIT, y cualquier otro es una falla con su código.
    /// </summary>
    private static ArcaServiceReply<T>? ErrorReply<T>(XElement? response, T? empty)
    {
        int[] codes = [.. Messages(response?.Element(Fev1 + "Errors")?.Elements(Fev1 + "Err")).Select(error => error.Code)];
        if (codes.Contains(NoResults)) return ArcaServiceReply.Ready(empty)!;
        if (codes.Any(code => code is 600 or 601)) return ArcaServiceReply.Failed<T>(ArcaServiceCheckStatus.NotAuthorized);
        return codes.Length > 0 ? ArcaServiceReply.Failed<T>(ArcaServiceCheckStatus.Failed, $"{Service}.{codes[0]}") : null;
    }

    private static IReadOnlyList<ArcaMessage> Messages(IEnumerable<XElement>? elements) =>
        [.. elements?.Select(element => Number(element.Element(Fev1 + "Code")) is { } code
            ? new ArcaMessage(code, element.Element(Fev1 + "Msg")?.Value.Trim() ?? string.Empty) : null).OfType<ArcaMessage>() ?? []];

    private static XElement Auth(ArcaCertificateRef certificate, ArcaTicketOutcome.Ready ticket) => new(Fev1 + "Auth",
        new XElement(Fev1 + "Token", ticket.Token), new XElement(Fev1 + "Sign", ticket.Sign), new XElement(Fev1 + "Cuit", certificate.Cuit));

    private static ArcaPointOfSale? PointOfSale(XElement point) => Number(point.Element(Fev1 + "Nro")) is { } number
        ? new ArcaPointOfSale(number, point.Element(Fev1 + "EmisionTipo")?.Value.Trim() ?? string.Empty,
            string.Equals(point.Element(Fev1 + "Bloqueado")?.Value.Trim(), "S", StringComparison.Ordinal), Date(point.Element(Fev1 + "FchBaja")))
        : null;

    private static XElement? Optional(string name, DateOnly? date) => date is { } value ? new XElement(Fev1 + name, Format(value)) : null;

    private static string Format(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Format(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    // «NULL» o vacío si no tiene fecha; si no, la fecha como aaaammdd.
    private static DateOnly? Date(XElement? value) =>
        DateOnly.TryParseExact(value?.Value.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static int? Number(XElement? value) =>
        int.TryParse(value?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static long? LongValue(XElement? value) =>
        long.TryParse(value?.Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static decimal? Amount(XElement? value) =>
        decimal.TryParse(value?.Value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

    private static string? Text(XElement? value) => string.IsNullOrWhiteSpace(value?.Value) ? null : value.Value.Trim();

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
