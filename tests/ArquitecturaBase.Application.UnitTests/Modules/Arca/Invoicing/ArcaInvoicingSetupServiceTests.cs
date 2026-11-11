using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;
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

    private static readonly DateOnly ActivityStart = new(2019, 3, 1);

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

        var result = await Setup(host).ChoosePointOfSaleAsync(new ChooseArcaPointOfSaleRequest(number, 0), Ct);

        Assert.Equal(ArcaInvoicingErrors.PointOfSaleUnavailable, result.Error);
        Assert.Null(host.Store.Settings);
    }

    [Fact]
    public async Task Choosing_a_valid_point_saves_it_with_its_first_revision_and_reevaluates_the_connection()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        host.InvoicingFamily.Complete = () => host.Store.Settings?.PointOfSale is not null;
        Assert.Equal(ArcaConnectionStatus.Unverified, (await host.Test.TestAsync(Ct)).Value.Connection.Status);
        host.Invoicing.Points = Points;
        Assert.Equal(new ArcaInvoicingSettingsResponse(null, null, null, 0), (await Setup(host).GetAsync(Ct)).Value);

        var settings = (await Setup(host).ChoosePointOfSaleAsync(new ChooseArcaPointOfSaleRequest(1, 0), Ct)).Value;

        Assert.Equal(new ArcaInvoicingSettingsResponse(1, null, null, 1), settings);
        Assert.Equal(new ArcaInvoicingSettingsResponse(1, null, null, 1), (await Setup(host).GetAsync(Ct)).Value);
        Assert.Equal(ArcaConnectionStatus.Active, host.Store.Connection!.Status);
    }

    [Fact]
    public async Task Voucher_data_is_saved_with_the_point_of_sale_and_read_back_without_calling_arca()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        host.Invoicing.Points = Points;
        await Setup(host).ChoosePointOfSaleAsync(new ChooseArcaPointOfSaleRequest(1, 0), Ct);
        host.Invoicing.Status = ArcaServiceCheckStatus.Unavailable;

        var saved = (await Setup(host).SetVoucherDataAsync(new SetArcaVoucherDataRequest(" 901-123456-7 ", ActivityStart, 1), Ct)).Value;

        var expected = new ArcaInvoicingSettingsResponse(1, "901-123456-7", ActivityStart, 2);
        Assert.Equal(expected, saved);
        Assert.Equal(expected, (await Setup(host).GetAsync(Ct)).Value);
        host.Invoicing.Status = ArcaServiceCheckStatus.Ready;
        Assert.Equal(expected with { Revision = 3 }, (await Setup(host).ChoosePointOfSaleAsync(new ChooseArcaPointOfSaleRequest(1, 2), Ct)).Value);
    }

    [Fact]
    public async Task Voucher_data_before_the_point_of_sale_creates_the_settings()
    {
        using var host = new ArcaApplicationHost();
        Assert.Equal(ArcaConnectionErrors.NotConnected, (await Setup(host).SetVoucherDataAsync(new SetArcaVoucherDataRequest("901-123456-7", ActivityStart, 0), Ct)).Error);
        await host.StartAndUploadAsync("wsfe");

        var saved = (await Setup(host).SetVoucherDataAsync(new SetArcaVoucherDataRequest("901-123456-7", ActivityStart, 0), Ct)).Value;

        Assert.Equal(new ArcaInvoicingSettingsResponse(null, "901-123456-7", ActivityStart, 1), saved);
    }

    [Fact]
    public async Task A_save_with_a_stale_revision_is_a_conflict_and_keeps_what_was_saved()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        host.Invoicing.Points = Points;
        await Setup(host).SetVoucherDataAsync(new SetArcaVoucherDataRequest("901-123456-7", ActivityStart, 0), Ct);

        var voucher = await Setup(host).SetVoucherDataAsync(new SetArcaVoucherDataRequest("902-000000-1", new DateOnly(2020, 1, 1), 0), Ct);
        var point = await Setup(host).ChoosePointOfSaleAsync(new ChooseArcaPointOfSaleRequest(1, 0), Ct);

        Assert.Equal(ArcaInvoicingErrors.SettingsRevisionConflict, voucher.Error);
        Assert.Equal(ArcaInvoicingErrors.SettingsRevisionConflict, point.Error);
        Assert.Equal(new ArcaInvoicingSettingsResponse(null, "901-123456-7", ActivityStart, 1), (await Setup(host).GetAsync(Ct)).Value);
    }

    [Fact]
    public async Task Invalid_voucher_data_answers_by_field_before_opening_the_transaction()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        var transactions = host.Work.Transactions;

        var blank = Assert.IsType<ValidationError>((await Setup(host).SetVoucherDataAsync(new SetArcaVoucherDataRequest(" ", default, -1), Ct)).Error);
        var tooLong = Assert.IsType<ValidationError>((await Setup(host).SetVoucherDataAsync(new SetArcaVoucherDataRequest(new string('9', 31), ActivityStart, 0), Ct)).Error);

        Assert.Equal(["activityStartOn", "expectedRevision", "grossIncomeNumber"], blank.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(["grossIncomeNumber"], tooLong.Errors.Keys);
        Assert.Equal(transactions, host.Work.Transactions);
        Assert.Null(host.Store.Settings);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(99999, 0)]
    [InlineData(1, -1)]
    public async Task An_invalid_point_of_sale_answers_by_field_without_asking_arca(int number, long revision)
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync("wsfe");
        host.Invoicing.Status = ArcaServiceCheckStatus.Unavailable;

        var error = Assert.IsType<ValidationError>((await Setup(host).ChoosePointOfSaleAsync(new ChooseArcaPointOfSaleRequest(number, revision), Ct)).Error);

        Assert.Equal([revision < 0 ? "expectedRevision" : "pointOfSale"], error.Errors.Keys);
        Assert.Null(host.Store.Settings);
    }

    [Fact]
    public async Task The_requests_and_the_settings_do_not_print_the_gross_income_number()
    {
        var texts = new[]
        {
            new SetArcaVoucherDataRequest("901-123456-7", ActivityStart, 1).ToString(),
            new ArcaInvoicingSettingsResponse(5, "901-123456-7", ActivityStart, 2).ToString(),
        };

        Assert.All(texts, text => Assert.DoesNotContain("901-123456-7", text, StringComparison.Ordinal));
    }

    private static IArcaInvoicingSetupService Setup(ArcaApplicationHost host) => host.Provider.GetRequiredService<IArcaInvoicingSetupService>();
}
