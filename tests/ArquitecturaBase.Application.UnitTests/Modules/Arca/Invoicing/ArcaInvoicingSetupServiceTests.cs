using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaInvoicingSetupServiceTests
{
    private static readonly ArcaPointOfSale[] Points =
    [
        new(1, "CAE - Factura", false, null),
        new(2, "CAE - Factura", true, null),
        new(3, "CAE - Factura", false, new DateOnly(2025, 1, 1)),
    ];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Points_of_sale_leave_out_the_dropped_ones_and_mark_the_blocked_ones()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        host.Invoicing.Points = Points;

        var points = (await Setup(host).ListPointsOfSaleAsync(Ct)).Value;

        Assert.Equal([new ArcaPointOfSaleResponse(1, "CAE - Factura", false), new ArcaPointOfSaleResponse(2, "CAE - Factura", true)], points);
    }

    [Fact]
    public async Task A_ticket_or_an_answer_that_is_not_ready_is_a_connection_error()
    {
        using var host = new ArcaApplicationHost();
        Assert.Equal(ArcaConnectionErrors.NotConnected, (await Setup(host).ListPointsOfSaleAsync(Ct)).Error);
        await host.StartAndUploadAsync("wsfe");

        host.Tickets.Outcome = new ArcaTicketOutcome.NotAuthorized();
        Assert.Equal(ArcaConnectionErrors.ServiceNotAuthorized, (await Setup(host).ListPointsOfSaleAsync(Ct)).Error);
        host.Tickets.Outcome = new ArcaTicketOutcome.NotDelegated();
        Assert.Equal(ArcaConnectionErrors.ServiceNotDelegated, (await Setup(host).ListPointsOfSaleAsync(Ct)).Error);
        host.Tickets.Outcome = new ArcaTicketOutcome.Blocked(ArcaApplicationHost.NowUtc.AddMinutes(10));
        Assert.Equal(600, (await Setup(host).ListPointsOfSaleAsync(Ct)).Error.Metadata!["retryAfter"]);
        host.Tickets.Outcome = new ArcaTicketOutcome.Ready("token", "sign", ArcaApplicationHost.NowUtc.AddHours(12));
        host.Invoicing.Status = ArcaServiceCheckStatus.Unavailable;
        Assert.Equal(ArcaConnectionErrors.Unavailable, (await Setup(host).ListPointsOfSaleAsync(Ct)).Error);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(9)]
    public async Task Choosing_a_point_that_arca_does_not_offer_or_is_blocked_is_unavailable(int number)
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        host.Invoicing.Points = Points;

        var result = await Setup(host).ChoosePointOfSaleAsync(number, Ct);

        Assert.Equal(ArcaInvoicingErrors.PointOfSaleUnavailable, result.Error);
        Assert.Null(host.Store.Settings);
    }

    [Fact]
    public async Task Choosing_a_valid_point_saves_it_and_reevaluates_the_connection()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        host.InvoicingFamily.Complete = () => host.Store.Settings?.PointOfSale is not null;
        Assert.Equal(ArcaConnectionStatus.Unverified, (await host.Test.TestAsync(Ct)).Value.Connection.Status);
        host.Invoicing.Points = Points;

        var settings = (await Setup(host).ChoosePointOfSaleAsync(1, Ct)).Value;

        Assert.Equal(new ArcaInvoicingSettingsResponse(1), settings);
        Assert.Equal(new ArcaInvoicingSettingsResponse(1), (await Setup(host).GetAsync(Ct)).Value);
        Assert.Equal(ArcaConnectionStatus.Active, host.Store.Connection!.Status);
    }

    private static IArcaInvoicingSetupService Setup(ArcaApplicationHost host) => host.Provider.GetRequiredService<IArcaInvoicingSetupService>();
}
