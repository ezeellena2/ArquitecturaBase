using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaVoucherOrderPolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    [Fact]
    public void A_valid_order_gets_its_lines_rounded_half_even()
    {
        var lines = ArcaVoucherOrderPolicy.Lines(Order(), Today);

        Assert.Equal([new ArcaVoucherLine("Semilla de soja", 10m, 52000m, 520000m), new ArcaVoucherLine("Flete", 1.5m, 0.125m, 0.19m)], lines.Value);
    }

    [Fact]
    public void A_final_consumer_without_a_document_is_valid()
    {
        Assert.True(ArcaVoucherOrderPolicy.Lines(Order() with { Receiver = new(99, 0, "Consumidor final", 5, null) }, Today).IsSuccess);
    }

    [Theory]
    [InlineData(80, 30712345672L, 1)]
    [InlineData(99, 30712345671L, 5)]
    [InlineData(99, 0L, 1)]
    [InlineData(96, 0L, 5)]
    [InlineData(1, 30712345671L, 1)]
    public void A_receiver_with_an_invalid_document_is_rejected(int documentType, long number, int condition)
    {
        var order = Order() with { Receiver = new(documentType, number, "Agro del Sur S.A.", condition, null) };

        Assert.Equal(ArcaInvoicingErrors.ReceiverInvalid, ArcaVoucherOrderPolicy.Lines(order, Today).Error);
    }

    [Fact]
    public void A_receiver_needs_a_name()
    {
        Assert.Equal(ArcaInvoicingErrors.ReceiverInvalid, ArcaVoucherOrderPolicy.Lines(Order() with { Receiver = new(80, 30712345671, " ", 1, null) }, Today).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void The_concept_is_products_services_or_both(int concept)
    {
        Assert.Equal(ArcaInvoicingErrors.ConceptInvalid, ArcaVoucherOrderPolicy.Lines(Order() with { Concept = concept }, Today).Error);
    }

    [Fact]
    public void A_service_needs_its_period_and_payment_due_date_in_order()
    {
        var service = Order() with { Concept = 2, ServiceFrom = new(2026, 10, 1), ServiceTo = new(2026, 10, 31), PaymentDueOn = Today };
        Assert.True(ArcaVoucherOrderPolicy.Lines(service, Today).IsSuccess);

        Assert.Equal(ArcaInvoicingErrors.ServicePeriodInvalid, ArcaVoucherOrderPolicy.Lines(service with { PaymentDueOn = null }, Today).Error);
        Assert.Equal(ArcaInvoicingErrors.ServicePeriodInvalid, ArcaVoucherOrderPolicy.Lines(service with { ServiceTo = new(2026, 9, 30) }, Today).Error);
        Assert.Equal(ArcaInvoicingErrors.ServicePeriodInvalid, ArcaVoucherOrderPolicy.Lines(service with { PaymentDueOn = Today.AddDays(-1) }, Today).Error);
        Assert.Equal(ArcaInvoicingErrors.ServicePeriodInvalid, ArcaVoucherOrderPolicy.Lines(Order() with { ServiceFrom = Today }, Today).Error);
    }

    [Fact]
    public void A_date_that_arca_would_not_take_is_rejected_before_queuing()
    {
        Assert.Equal(ArcaInvoicingErrors.IssueDateOutOfRange, ArcaVoucherOrderPolicy.Lines(Order() with { IssueOn = Today.AddDays(-6) }, Today).Error);
    }

    [Theory]
    [InlineData("", 1, 1)]
    [InlineData("Flete", 0, 1)]
    [InlineData("Flete", 1, -1)]
    public void An_item_needs_a_description_a_positive_quantity_and_a_price(string description, int quantity, int price)
    {
        var order = Order() with { Items = [new ArcaVoucherOrderItem(description, quantity, price)], ExpectedTotal = null };

        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, ArcaVoucherOrderPolicy.Lines(order, Today).Error);
    }

    [Fact]
    public void An_order_needs_items_and_a_total_above_zero()
    {
        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, ArcaVoucherOrderPolicy.Lines(Order() with { Items = [], ExpectedTotal = null }, Today).Error);
        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid,
            ArcaVoucherOrderPolicy.Lines(Order() with { Items = [new ArcaVoucherOrderItem("Regalo", 1, 0)], ExpectedTotal = null }, Today).Error);
    }

    [Fact]
    public void A_total_that_does_not_match_the_server_is_not_queued()
    {
        Assert.Equal(ArcaInvoicingErrors.TotalMismatch, ArcaVoucherOrderPolicy.Lines(Order() with { ExpectedTotal = 520000.18m }, Today).Error);
    }

    private static ArcaVoucherOrder Order() => new(Guid.CreateVersion7(), new ArcaVoucherParty(80, 30712345671, "Agro del Sur S.A.", 1, "Ruta 8 km 225"),
        1, Today, null, null, null, [new ArcaVoucherOrderItem("Semilla de soja", 10m, 52000m), new ArcaVoucherOrderItem("Flete", 1.5m, 0.125m)], 520000.19m);
}
