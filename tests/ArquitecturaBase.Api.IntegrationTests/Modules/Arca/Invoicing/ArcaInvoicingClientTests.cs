using System.Xml.Linq;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Invoicing;

/// <summary>WSFEv1 contra FakeArca: FEDummy y FEParamGetPtosVenta, con los nombres del WSDL de homologación.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaInvoicingClientTests(ApiFactory factory)
{
    private static readonly XNamespace Fev1 = "http://ar.gov.afip.dif.FEV1/";
    private static readonly ArcaCertificateRef Certificate = new(Guid.Empty, Guid.Empty, "20000000060", ArcaEnvironment.Testing, "", "");
    private static readonly ArcaTicketOutcome.Ready Ticket = new("the-token", "the-sign", DateTime.MaxValue);

    [Fact]
    public async Task The_server_is_up_when_every_part_answers_ok()
    {
        Arca();

        var reply = await Client().CheckServerAsync(ArcaEnvironment.Testing, TestContext.Current.CancellationToken);

        Assert.Equal((ArcaServiceCheckStatus.Ready, true), (reply.Status, reply.Value));
    }

    [Fact]
    public async Task The_server_is_down_when_a_part_does_not_answer_ok()
    {
        Arca().ServerStatus = ("OK", "NO", "OK");

        var reply = await Client().CheckServerAsync(ArcaEnvironment.Testing, TestContext.Current.CancellationToken);

        Assert.Equal((ArcaServiceCheckStatus.Ready, false), (reply.Status, reply.Value));
    }

    [Fact]
    public async Task A_503_is_unavailable()
    {
        Arca().Fail(FakeArca.InvoicingService, FakeArcaFailure.ServiceUnavailable);

        var reply = await Client().CheckServerAsync(ArcaEnvironment.Testing, TestContext.Current.CancellationToken);

        Assert.Equal(ArcaServiceCheckStatus.Unavailable, reply.Status);
    }

    [Fact]
    public async Task Points_of_sale_come_with_their_type_block_and_drop_date_and_the_request_carries_the_auth()
    {
        var arca = Arca();
        arca.PointsOfSale = [new(1, "CAE - Factura", "N", "NULL"), new(2, "CAEA", "S", "20250101")];

        var reply = await Client().ListPointsOfSaleAsync(Certificate, Ticket, TestContext.Current.CancellationToken);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        Assert.Equal([new ArcaPointOfSale(1, "CAE - Factura", false, null), new ArcaPointOfSale(2, "CAEA", true, new DateOnly(2025, 1, 1))], reply.Value);
        var auth = arca.LastRequest("FEParamGetPtosVenta")?.Element(Fev1 + "Auth");
        Assert.Equal(("the-token", "the-sign", "20000000060"),
            (auth?.Element(Fev1 + "Token")?.Value, auth?.Element(Fev1 + "Sign")?.Value, auth?.Element(Fev1 + "Cuit")?.Value));
    }

    [Fact]
    public async Task No_results_is_an_empty_list()
    {
        Arca().PointsOfSaleError = 602;

        var reply = await Client().ListPointsOfSaleAsync(Certificate, Ticket, TestContext.Current.CancellationToken);

        Assert.Equal(ArcaServiceCheckStatus.Ready, reply.Status);
        Assert.Empty(reply.Value!);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(601)]
    public async Task A_rejected_token_or_cuit_is_not_authorized(int code)
    {
        Arca().PointsOfSaleError = code;

        var reply = await Client().ListPointsOfSaleAsync(Certificate, Ticket, TestContext.Current.CancellationToken);

        Assert.Equal(ArcaServiceCheckStatus.NotAuthorized, reply.Status);
    }

    [Fact]
    public async Task Another_error_fails_with_its_code()
    {
        Arca().PointsOfSaleError = 1000;

        var reply = await Client().ListPointsOfSaleAsync(Certificate, Ticket, TestContext.Current.CancellationToken);

        Assert.Equal((ArcaServiceCheckStatus.Failed, "wsfe.1000"), (reply.Status, reply.FailureCode));
    }

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }

    private IArcaInvoicingClient Client() => factory.Services.GetRequiredService<IArcaInvoicingClient>();
}
