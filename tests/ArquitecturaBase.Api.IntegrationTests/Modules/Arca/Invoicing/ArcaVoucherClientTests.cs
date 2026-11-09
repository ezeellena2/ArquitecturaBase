using System.Xml.Linq;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>
/// La emisión de WSFEv1 contra FakeArca: <c>FECompUltimoAutorizado</c>, <c>FECAESolicitar</c>, <c>FECompConsultar</c> y las
/// tablas, con los nombres y el orden del manual 4.7. Lo que no dice si se emitió nunca vuelve como rechazo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaVoucherClientTests(ApiFactory factory)
{
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";
    private static readonly ArcaCertificateRef Certificate = new(Guid.Empty, Guid.Empty, "27287654338", ArcaEnvironment.Testing, "", "");
    private static readonly ArcaTicketOutcome.Ready Ticket = new("the-token", "the-sign", DateTime.MaxValue);
    private static readonly DateOnly IssuedOn = new(2026, 11, 9);

    [Fact]
    public async Task The_last_authorized_number_is_asked_with_the_auth_the_point_of_sale_and_the_type()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 41);

        var reply = await Client().GetLastAuthorizedAsync(Certificate, Ticket, 4, 11, Ct);

        Assert.Equal((ArcaServiceCheckStatus.Ready, 41L), (reply.Status, reply.Value));
        var request = arca.LastRequest("FECompUltimoAutorizado")!;
        Assert.Equal(("the-token", "the-sign", "27287654338", "4", "11"),
            (request.Element(Fev1 + "Auth")?.Element(Fev1 + "Token")?.Value, request.Element(Fev1 + "Auth")?.Element(Fev1 + "Sign")?.Value,
             request.Element(Fev1 + "Auth")?.Element(Fev1 + "Cuit")?.Value, request.Element(Fev1 + "PtoVta")?.Value, request.Element(Fev1 + "CbteTipo")?.Value));
    }

    [Fact]
    public async Task The_next_number_is_approved_with_its_cae_and_due_date()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);

        var reply = await Client().RequestAuthorizationAsync(Certificate, Ticket, Invoice(12), Ct);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        Assert.Equal(ArcaAuthorizationResult.Approved, reply.Value!.Result);
        Assert.Equal(arca.Vouchers.Issued.Single().Cae, reply.Value.Cae);
        Assert.Equal(new DateOnly(2026, 11, 19), reply.Value.CaeDueOn);
    }

    [Fact]
    public async Task A_class_c_invoice_goes_with_only_net_and_total_in_the_manual_order()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);

        await Client().RequestAuthorizationAsync(Certificate, Ticket, Invoice(12), Ct);

        var request = arca.LastRequest("FECAESolicitar")!.Element(Fev1 + "FeCAEReq")!;
        var head = request.Element(Fev1 + "FeCabReq")!;
        Assert.Equal(("1", "4", "11"), (head.Element(Fev1 + "CantReg")?.Value, head.Element(Fev1 + "PtoVta")?.Value, head.Element(Fev1 + "CbteTipo")?.Value));
        var detail = request.Element(Fev1 + "FeDetReq")!.Element(Fev1 + "FECAEDetRequest")!;
        Assert.Equal(
            ["Concepto", "DocTipo", "DocNro", "CbteDesde", "CbteHasta", "CbteFch", "ImpTotal", "ImpTotConc", "ImpNeto", "ImpOpEx", "ImpTrib", "ImpIVA",
             "MonId", "MonCotiz", "CondicionIVAReceptorId"],
            detail.Elements().Select(element => element.Name.LocalName));
        Assert.Equal(["1", "80", "30712345671", "12", "12", "20261109", "605500.00", "0.00", "605500.00", "0.00", "0.00", "0.00", "PES", "1", "1"],
            detail.Elements().Select(element => element.Value));
    }

    [Fact]
    public async Task A_service_carries_its_period_and_payment_due_date()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 0);

        await Client().RequestAuthorizationAsync(Certificate, Ticket,
            Invoice(1) with { Concept = 2, ServiceFrom = new(2026, 10, 1), ServiceTo = new(2026, 10, 31), PaymentDueOn = new(2026, 11, 20) }, Ct);

        var detail = arca.LastRequest("FECAESolicitar")!.Descendants(Fev1 + "FECAEDetRequest").Single();
        Assert.Equal(("20261001", "20261031", "20261120"),
            (detail.Element(Fev1 + "FchServDesde")?.Value, detail.Element(Fev1 + "FchServHasta")?.Value, detail.Element(Fev1 + "FchVtoPago")?.Value));
        Assert.Equal("ImpIVA", detail.Element(Fev1 + "FchServDesde")!.ElementsBeforeSelf().Last().Name.LocalName);
    }

    [Fact]
    public async Task A_credit_note_carries_the_invoice_it_cancels()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 13, 0);

        await Client().RequestAuthorizationAsync(Certificate, Ticket,
            Invoice(1) with { VoucherType = 13, Associated = [new ArcaAssociatedVoucher(11, 4, 12, "27287654338", IssuedOn)] }, Ct);

        var associated = arca.LastRequest("FECAESolicitar")!.Descendants(Fev1 + "CbteAsoc").Single();
        Assert.Equal(["11", "4", "12", "27287654338", "20261109"], associated.Elements().Select(element => element.Value));
        Assert.Equal(["Tipo", "PtoVta", "Nro", "Cuit", "CbteFch"], associated.Elements().Select(element => element.Name.LocalName));
        Assert.Equal("CondicionIVAReceptorId", associated.Parent!.ElementsBeforeSelf().Last().Name.LocalName);
    }

    [Fact]
    public async Task A_number_that_is_not_the_next_is_uncertain_and_carries_the_10016()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 12);

        var reply = await Client().RequestAuthorizationAsync(Certificate, Ticket, Invoice(12), Ct);

        Assert.Equal(ArcaAuthorizationResult.Uncertain, reply.Value!.Result);
        Assert.Contains(reply.Value.Observations, observation => observation.Code == 10016);
    }

    [Fact]
    public async Task A_validation_failure_is_rejected_with_the_codes_of_arca()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);
        arca.Vouchers.Enqueue(FakeArcaVoucherFault.Reject);

        var reply = await Client().RequestAuthorizationAsync(Certificate, Ticket, Invoice(12), Ct);

        Assert.Equal(ArcaAuthorizationResult.Rejected, reply.Value!.Result);
        Assert.Equal([10015], reply.Value.Observations.Select(observation => observation.Code));
        Assert.Null(reply.Value.Cae);
    }

    [Theory]
    [InlineData(502)]
    [InlineData(500)]
    public async Task An_internal_error_of_arca_is_uncertain(int code)
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);
        arca.Vouchers.Enqueue(code == 502 ? FakeArcaVoucherFault.TransactionActive : FakeArcaVoucherFault.InternalError);

        var reply = await Client().RequestAuthorizationAsync(Certificate, Ticket, Invoice(12), Ct);

        Assert.Equal(ArcaAuthorizationResult.Uncertain, reply.Value!.Result);
        Assert.Equal([code], reply.Value.Errors.Select(error => error.Code));
    }

    [Fact]
    public async Task A_response_that_does_not_arrive_is_unavailable_even_when_arca_issued_it()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);
        arca.Vouchers.Enqueue(FakeArcaVoucherFault.LoseResponseAfterAuthorizing);

        var reply = await Client().RequestAuthorizationAsync(Certificate, Ticket, Invoice(12), Ct);

        Assert.Equal(ArcaServiceCheckStatus.Unavailable, reply.Status);
        Assert.Single(arca.Vouchers.Issued);
    }

    [Fact]
    public async Task An_issued_voucher_is_found_with_what_was_sent_and_its_authorization()
    {
        var arca = Arca();
        arca.Vouchers.SetLast(4, 11, 11);
        await Client().RequestAuthorizationAsync(Certificate, Ticket, Invoice(12), Ct);

        var reply = await Client().FindVoucherAsync(Certificate, Ticket, 4, 11, 12, Ct);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        Assert.Equal(new ArcaIssuedVoucher(1, 80, 30712345671, IssuedOn, 605500m, "A", arca.Vouchers.Issued.Single().Cae, new DateOnly(2026, 11, 19)),
            reply.Value);
        var query = arca.LastRequest("FECompConsultar")!.Element(Fev1 + "FeCompConsReq")!;
        Assert.Equal(["CbteTipo", "CbteNro", "PtoVta"], query.Elements().Select(element => element.Name.LocalName));
    }

    [Fact]
    public async Task A_missing_number_is_found_as_nothing()
    {
        Arca();

        var reply = await Client().FindVoucherAsync(Certificate, Ticket, 4, 11, 12, Ct);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        Assert.Null(reply.Value);
    }

    [Fact]
    public async Task A_query_that_does_not_arrive_is_unavailable()
    {
        Arca().Vouchers.LoseNextConsult();

        var reply = await Client().FindVoucherAsync(Certificate, Ticket, 4, 11, 12, Ct);

        Assert.Equal(ArcaServiceCheckStatus.Unavailable, reply.Status);
    }

    [Fact]
    public async Task The_receiver_vat_conditions_come_with_each_voucher_class()
    {
        Arca();

        var reply = await Client().ListParametersAsync(Certificate, Ticket, ArcaParameterTable.ReceiverVatConditions, Ct);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        Assert.Contains(new ArcaParameter("5", "Consumidor Final", null, null, "C"), reply.Value!);
        Assert.Equal(FakeArcaVouchers.DefaultConditions.Count, reply.Value!.Count);
    }

    [Theory]
    [InlineData(ArcaParameterTable.DocumentTypes, "FEParamGetTiposDoc", "80", "CUIT")]
    [InlineData(ArcaParameterTable.Concepts, "FEParamGetTiposConcepto", "2", "Servicios")]
    [InlineData(ArcaParameterTable.Currencies, "FEParamGetTiposMonedas", "PES", "Pesos Argentinos")]
    [InlineData(ArcaParameterTable.VatRates, "FEParamGetTiposIva", "5", "21%")]
    public async Task A_parameter_table_comes_with_its_validity(ArcaParameterTable table, string operation, string id, string description)
    {
        var arca = Arca();

        var reply = await Client().ListParametersAsync(Certificate, Ticket, table, Ct);

        Assert.Contains(new ArcaParameter(id, description, new DateOnly(2010, 9, 17), null), reply.Value!);
        Assert.NotNull(arca.LastRequest(operation));
    }

    [Fact]
    public async Task An_expired_parameter_keeps_its_end_date()
    {
        Arca();

        var reply = await Client().ListParametersAsync(Certificate, Ticket, ArcaParameterTable.VatRates, Ct);

        Assert.Contains(new ArcaParameter("6", "27%", new DateOnly(2010, 9, 17), new DateOnly(2020, 12, 31)), reply.Value!);
    }

    private static ArcaVoucherRequest Invoice(long number) => new(4, 11, number, 1, 80, 30712345671, IssuedOn,
        new ArcaVoucherAmounts(605500m, 0m, 605500m, 0m, 0m, 0m, []), null, null, null, "PES", 1m, 1, []);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }

    private IArcaInvoicingClient Client() => factory.Services.GetRequiredService<IArcaInvoicingClient>();
}
