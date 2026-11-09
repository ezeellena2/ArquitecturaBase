using System.Collections.Concurrent;
using System.Globalization;
using System.Xml.Linq;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;

/// <summary>Lo que hace el ARCA simulado con el próximo <c>FECAESolicitar</c>, en lugar de responder normal.</summary>
internal enum FakeArcaVoucherFault
{
    /// <summary>Autoriza y guarda el comprobante, pero la respuesta no llega (se corta la conexión).</summary>
    LoseResponseAfterAuthorizing,

    /// <summary>No autoriza y la respuesta no llega.</summary>
    LoseRequest,

    /// <summary>No autoriza y responde el error 502 «Transacción activa».</summary>
    TransactionActive,

    /// <summary>No autoriza y responde el error 500.</summary>
    InternalError,

    /// <summary>Rechaza por validación: resultado R con la observación 10015.</summary>
    Reject,
}

/// <summary>Un comprobante que el ARCA simulado autorizó.</summary>
internal sealed record FakeArcaVoucher(int PointOfSale, int Type, long Number, int Concept, int DocumentType, long DocumentNumber, string IssuedOn,
    decimal Total, string Cae, string CaeDueOn);

/// <summary>
/// La emisión de WSFEv1 del ARCA simulado: el último número por punto de venta y tipo, los comprobantes autorizados, el
/// 10016 cuando el número no es el siguiente y las fallas programadas de <c>FECAESolicitar</c> y <c>FECompConsultar</c>.
/// También responde las tablas de parámetros. Es parte de <see cref="FakeArca"/> y se limpia con su <c>Reset</c>.
/// </summary>
internal sealed class FakeArcaVouchers
{
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";
    private readonly Lock gate = new();
    private readonly Dictionary<(int, int), long> last = [];
    private readonly List<FakeArcaVoucher> issued = [];
    private readonly ConcurrentQueue<FakeArcaVoucherFault> faults = new();
    private readonly ConcurrentQueue<bool> lostConsults = new();
    private int caeSequence;

    public IReadOnlyList<FakeArcaVoucher> Issued
    {
        get { lock (gate) return [.. issued]; }
    }

    /// <summary>Lo que devuelve <c>FEParamGetCondicionIvaReceptor</c>: código, texto y clase.</summary>
    public IReadOnlyList<(int Id, string Description, string Class)> ReceiverVatConditions { get; set; } = DefaultConditions;

    public static readonly IReadOnlyList<(int, string, string)> DefaultConditions =
    [
        (1, "IVA Responsable Inscripto", "A"), (1, "IVA Responsable Inscripto", "C"), (4, "IVA Sujeto Exento", "B"), (4, "IVA Sujeto Exento", "C"),
        (5, "Consumidor Final", "B"), (5, "Consumidor Final", "C"), (6, "Responsable Monotributo", "A"), (6, "Responsable Monotributo", "C"),
    ];

    public void SetLast(int pointOfSale, int type, long number)
    {
        lock (gate) last[(pointOfSale, type)] = number;
    }

    public long Last(int pointOfSale, int type)
    {
        lock (gate) return last.GetValueOrDefault((pointOfSale, type));
    }

    /// <summary>Lo que ARCA tiene de afuera: otro sistema emitió ese número con otros datos.</summary>
    public void IssueElsewhere(int pointOfSale, int type, long number, long documentNumber, decimal total, string issuedOn)
    {
        lock (gate)
        {
            issued.Add(new(pointOfSale, type, number, 1, 80, documentNumber, issuedOn, total, NextCae(), issuedOn));
            last[(pointOfSale, type)] = Math.Max(last.GetValueOrDefault((pointOfSale, type)), number);
        }
    }

    public void Enqueue(FakeArcaVoucherFault fault) => faults.Enqueue(fault);

    /// <summary>La próxima consulta (<c>FECompConsultar</c>) no recibe respuesta.</summary>
    public void LoseNextConsult() => lostConsults.Enqueue(true);

    public void Reset()
    {
        lock (gate)
        {
            last.Clear();
            issued.Clear();
        }

        faults.Clear();
        lostConsults.Clear();
        ReceiverVatConditions = DefaultConditions;
    }

    public XElement LastAuthorized(XElement operation)
    {
        var (pointOfSale, type) = (Int(operation.Element(Fev1 + "PtoVta")), Int(operation.Element(Fev1 + "CbteTipo")));
        return new XElement(Fev1 + "FECompUltimoAutorizadoResponse", new XElement(Fev1 + "FECompUltimoAutorizadoResult",
            new XElement(Fev1 + "PtoVta", pointOfSale), new XElement(Fev1 + "CbteTipo", type), new XElement(Fev1 + "CbteNro", Last(pointOfSale, type))));
    }

    /// <summary>La respuesta de <c>FECAESolicitar</c>, o null si se corta la conexión.</summary>
    public XElement? Authorize(XElement operation)
    {
        var request = operation.Element(Fev1 + "FeCAEReq")!;
        var head = request.Element(Fev1 + "FeCabReq")!;
        var detail = request.Element(Fev1 + "FeDetReq")!.Element(Fev1 + "FECAEDetRequest")!;
        var (pointOfSale, type) = (Int(head.Element(Fev1 + "PtoVta")), Int(head.Element(Fev1 + "CbteTipo")));
        var number = Long(detail.Element(Fev1 + "CbteDesde"));
        FakeArcaVoucherFault? fault = faults.TryDequeue(out var next) ? next : null;

        switch (fault)
        {
            case FakeArcaVoucherFault.LoseRequest:
                return null;
            case FakeArcaVoucherFault.TransactionActive:
                return Errors(pointOfSale, type, 502, "Error interno de base de datos - Autorizador - Transaccion Activa");
            case FakeArcaVoucherFault.InternalError:
                return Errors(pointOfSale, type, 500, "Error interno de aplicacion");
            case FakeArcaVoucherFault.Reject:
                return Detail(pointOfSale, type, detail, number, "R", null, null, (10015, "El numero de documento del receptor no es valido"));
        }

        FakeArcaVoucher? voucher = null;
        lock (gate)
        {
            if (number == last.GetValueOrDefault((pointOfSale, type)) + 1)
            {
                var issuedOn = detail.Element(Fev1 + "CbteFch")!.Value;
                var due = DateOnly.ParseExact(issuedOn, "yyyyMMdd", CultureInfo.InvariantCulture).AddDays(10).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                voucher = new(pointOfSale, type, number, Int(detail.Element(Fev1 + "Concepto")), Int(detail.Element(Fev1 + "DocTipo")),
                    Long(detail.Element(Fev1 + "DocNro")), issuedOn, decimal.Parse(detail.Element(Fev1 + "ImpTotal")!.Value, CultureInfo.InvariantCulture),
                    NextCae(), due);
                issued.Add(voucher);
                last[(pointOfSale, type)] = number;
            }
        }

        if (fault == FakeArcaVoucherFault.LoseResponseAfterAuthorizing) return null;
        return voucher is null
            ? Detail(pointOfSale, type, detail, number, "R", null, null, (10016, $"comp. {number} no coincide con el proximo a autorizar"))
            : Detail(pointOfSale, type, detail, number, "A", voucher.Cae, voucher.CaeDueOn, null);
    }

    /// <summary>La respuesta de <c>FECompConsultar</c>, o null si se corta la conexión.</summary>
    public XElement? Find(XElement operation)
    {
        if (lostConsults.TryDequeue(out _)) return null;
        var query = operation.Element(Fev1 + "FeCompConsReq")!;
        var (pointOfSale, type, number) = (Int(query.Element(Fev1 + "PtoVta")), Int(query.Element(Fev1 + "CbteTipo")), Long(query.Element(Fev1 + "CbteNro")));
        FakeArcaVoucher? voucher;
        lock (gate) voucher = issued.FirstOrDefault(v => v.PointOfSale == pointOfSale && v.Type == type && v.Number == number);
        var result = voucher is null
            ? new XElement(Fev1 + "FECompConsultarResult", new XElement(Fev1 + "Errors", new XElement(Fev1 + "Err",
                new XElement(Fev1 + "Code", 602), new XElement(Fev1 + "Msg", "No existen datos en nuestros registros para los parametros ingresados."))))
            : new XElement(Fev1 + "FECompConsultarResult", new XElement(Fev1 + "ResultGet",
                new XElement(Fev1 + "Concepto", voucher.Concept), new XElement(Fev1 + "DocTipo", voucher.DocumentType),
                new XElement(Fev1 + "DocNro", voucher.DocumentNumber), new XElement(Fev1 + "CbteDesde", voucher.Number),
                new XElement(Fev1 + "CbteHasta", voucher.Number), new XElement(Fev1 + "CbteFch", voucher.IssuedOn),
                new XElement(Fev1 + "ImpTotal", voucher.Total.ToString(CultureInfo.InvariantCulture)), new XElement(Fev1 + "MonId", "PES"),
                new XElement(Fev1 + "Resultado", "A"), new XElement(Fev1 + "CodAutorizacion", voucher.Cae), new XElement(Fev1 + "EmisionTipo", "CAE"),
                new XElement(Fev1 + "FchVto", voucher.CaeDueOn), new XElement(Fev1 + "PtoVta", pointOfSale), new XElement(Fev1 + "CbteTipo", type)));
        return new XElement(Fev1 + "FECompConsultarResponse", result);
    }

    public XElement Parameters(string operation) => operation switch
    {
        "FEParamGetCondicionIvaReceptor" => Rows(operation, "CondicionIvaReceptor", ReceiverVatConditions.Select(c =>
            new object[] { new XElement(Fev1 + "Id", c.Id), new XElement(Fev1 + "Desc", c.Description), new XElement(Fev1 + "Cmp_Clase", c.Class) })),
        "FEParamGetTiposDoc" => Rows(operation, "DocTipo", Dated([("80", "CUIT"), ("86", "CUIL"), ("96", "DNI"), ("99", "Doc. (Otro)")])),
        "FEParamGetTiposConcepto" => Rows(operation, "ConceptoTipo", Dated([("1", "Producto"), ("2", "Servicios"), ("3", "Productos y Servicios")])),
        "FEParamGetTiposMonedas" => Rows(operation, "Moneda", Dated([("PES", "Pesos Argentinos"), ("DOL", "Dólar Estadounidense")])),
        _ => Rows(operation, "IvaTipo", Dated([("3", "0%"), ("4", "10.5%"), ("5", "21%"), ("6", "27%")], expiredId: "6")),
    };

    private static IEnumerable<object[]> Dated(IEnumerable<(string Id, string Description)> rows, string? expiredId = null) => rows.Select(row =>
        new object[] { new XElement(Fev1 + "Id", row.Id), new XElement(Fev1 + "Desc", row.Description), new XElement(Fev1 + "FchDesde", "20100917"),
            new XElement(Fev1 + "FchHasta", row.Id == expiredId ? "20201231" : "NULL") });

    private static XElement Rows(string operation, string row, IEnumerable<object[]> rows) =>
        new(Fev1 + operation + "Response", new XElement(Fev1 + operation + "Result", new XElement(Fev1 + "ResultGet", rows.Select(r => new XElement(Fev1 + row, r)))));

    private static XElement Errors(int pointOfSale, int type, int code, string message) =>
        new(Fev1 + "FECAESolicitarResponse", new XElement(Fev1 + "FECAESolicitarResult",
            new XElement(Fev1 + "FeCabResp", new XElement(Fev1 + "PtoVta", pointOfSale), new XElement(Fev1 + "CbteTipo", type)),
            new XElement(Fev1 + "Errors", new XElement(Fev1 + "Err", new XElement(Fev1 + "Code", code), new XElement(Fev1 + "Msg", message)))));

    private static XElement Detail(int pointOfSale, int type, XElement request, long number, string result, string? cae, string? due, (int Code, string Message)? observation) =>
        new(Fev1 + "FECAESolicitarResponse", new XElement(Fev1 + "FECAESolicitarResult",
            new XElement(Fev1 + "FeCabResp", new XElement(Fev1 + "PtoVta", pointOfSale), new XElement(Fev1 + "CbteTipo", type),
                new XElement(Fev1 + "CantReg", 1), new XElement(Fev1 + "Resultado", result), new XElement(Fev1 + "Reproceso", "N")),
            new XElement(Fev1 + "FeDetResp", new XElement(Fev1 + "FECAEDetResponse",
                new XElement(Fev1 + "Concepto", request.Element(Fev1 + "Concepto")!.Value), new XElement(Fev1 + "DocTipo", request.Element(Fev1 + "DocTipo")!.Value),
                new XElement(Fev1 + "DocNro", request.Element(Fev1 + "DocNro")!.Value), new XElement(Fev1 + "CbteDesde", number), new XElement(Fev1 + "CbteHasta", number),
                new XElement(Fev1 + "CbteFch", request.Element(Fev1 + "CbteFch")!.Value), new XElement(Fev1 + "Resultado", result),
                observation is { } obs ? new XElement(Fev1 + "Observaciones", new XElement(Fev1 + "Obs", new XElement(Fev1 + "Code", obs.Code), new XElement(Fev1 + "Msg", obs.Message))) : null,
                new XElement(Fev1 + "CAE", cae ?? string.Empty), new XElement(Fev1 + "CAEFchVto", due ?? string.Empty)))));

    private string NextCae() => "7" + Interlocked.Increment(ref caeSequence).ToString("D13", CultureInfo.InvariantCulture);

    private static int Int(XElement? value) => int.Parse(value!.Value, CultureInfo.InvariantCulture);

    private static long Long(XElement? value) => long.Parse(value!.Value, CultureInfo.InvariantCulture);
}
