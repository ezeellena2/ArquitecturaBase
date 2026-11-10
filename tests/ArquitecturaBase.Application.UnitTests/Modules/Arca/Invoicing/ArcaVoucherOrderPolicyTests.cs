using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

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
        var order = Order() with { ExpectedTotal = 520000.18m };

        Assert.Equal(ArcaInvoicingErrors.TotalMismatch, ForClass(ArcaVoucherClass.C, order).Error);
    }

    [Fact]
    public void The_lines_keep_the_vat_of_each_item()
    {
        var lines = ArcaVoucherOrderPolicy.Lines(Taxed(Order()), Today);

        Assert.Equal([new ArcaVoucherLine("Semilla de soja", 10m, 52000m, 520000m, Rate(5)), new ArcaVoucherLine("Flete", 1.5m, 0.125m, 0.19m, Rate(4))],
            lines.Value);
    }

    [Theory]
    [InlineData(ArcaVatTreatmentKind.Taxed, null)]
    [InlineData(ArcaVatTreatmentKind.Taxed, 7)]
    [InlineData(ArcaVatTreatmentKind.Exempt, 5)]
    [InlineData(ArcaVatTreatmentKind.NotTaxed, 3)]
    public void The_vat_of_an_item_is_a_rate_of_arca_exempt_or_not_taxed(ArcaVatTreatmentKind kind, int? rateId)
    {
        var order = Order() with { Items = [new ArcaVoucherOrderItem("Flete", 1m, 100m, new ArcaVoucherVat(kind, rateId))], ExpectedTotal = null };

        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, ArcaVoucherOrderPolicy.Lines(order, Today).Error);
    }

    [Fact]
    public void A_class_a_order_with_net_prices_adds_the_vat_of_each_rate()
    {
        var priced = ForClass(ArcaVoucherClass.A, Taxed(Order()) with { ExpectedTotal = 629200.21m });

        AssertAmounts(new ArcaVoucherAmounts(629200.21m, 0m, 520000.19m, 0m, 0m, 109200.02m, [new(4, 0.19m, 0.02m), new(5, 520000m, 109200m)]),
            priced.Value.Amounts);
        Assert.Equal([Rate(5), Rate(4)], priced.Value.Lines.Select(line => line.Vat));
    }

    [Fact]
    public void A_class_b_order_with_prices_with_vat_keeps_the_total_of_its_items()
    {
        var order = Order() with
        {
            Receiver = new(99, 0, "Consumidor final", 5, null),
            Items =
            [
                new ArcaVoucherOrderItem("Bolsa de alimento", 2m, 12100m, Rate(5)),
                new ArcaVoucherOrderItem("Libro", 1m, 15000m, new ArcaVoucherVat(ArcaVatTreatmentKind.Exempt)),
            ],
            ExpectedTotal = 39200m,
            PricesIncludeVat = true,
        };

        var priced = ForClass(ArcaVoucherClass.B, order);

        AssertAmounts(new ArcaVoucherAmounts(39200m, 0m, 20000m, 15000m, 0m, 4200m, [new(5, 20000m, 4200m)]), priced.Value.Amounts);
    }

    [Fact]
    public void A_class_a_or_b_order_needs_the_vat_of_every_item()
    {
        var order = Taxed(Order()) with { ExpectedTotal = null };
        var missing = order with { Items = [order.Items[0], order.Items[1] with { Vat = null }] };

        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, ForClass(ArcaVoucherClass.A, missing).Error);
        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, ForClass(ArcaVoucherClass.B, missing).Error);
    }

    [Fact]
    public void A_class_a_receiver_needs_a_cuit()
    {
        // 10013: la A va siempre con DocTipo 80.
        var order = Taxed(Order()) with { Receiver = new(96, 28765433, "Lucía Fernández", 6, null), ExpectedTotal = null };

        Assert.Equal(ArcaInvoicingErrors.ReceiverInvalid, ForClass(ArcaVoucherClass.A, order).Error);
        Assert.True(ForClass(ArcaVoucherClass.B, order with { Receiver = new(96, 28765433, "Lucía Fernández", 5, null) }).IsSuccess);
    }

    [Fact]
    public void A_class_c_order_drops_the_vat_and_totals_its_items()
    {
        var priced = ForClass(ArcaVoucherClass.C, Taxed(Order()) with { PricesIncludeVat = true });

        Assert.Equal(new ArcaVoucherAmounts(520000.19m, 0m, 520000.19m, 0m, 0m, 0m, []), priced.Value.Amounts);
        Assert.All(priced.Value.Lines, line => Assert.Null(line.Vat));
    }

    private static Domain.Results.Result<(IReadOnlyList<ArcaVoucherLine> Lines, ArcaVoucherAmounts Amounts)> ForClass(ArcaVoucherClass voucherClass,
        ArcaVoucherOrder order) => ArcaVoucherOrderPolicy.ForClass(voucherClass, order, ArcaVoucherOrderPolicy.Lines(order, Today).Value);

    private static ArcaVoucherVat Rate(int id) => new(ArcaVatTreatmentKind.Taxed, id);

    // Un record compara su lista por referencia: los importes y las alícuotas van por separado.
    internal static void AssertAmounts(ArcaVoucherAmounts expected, ArcaVoucherAmounts actual)
    {
        Assert.Equal(expected with { VatRates = [] }, actual with { VatRates = [] });
        Assert.Equal(expected.VatRates, actual.VatRates);
    }

    private static ArcaVoucherOrder Taxed(ArcaVoucherOrder order) =>
        order with { Items = [order.Items[0] with { Vat = Rate(5) }, order.Items[1] with { Vat = Rate(4) }] };

    private static ArcaVoucherOrder Order() => new(Guid.CreateVersion7(), new ArcaVoucherParty(80, 30712345671, "Agro del Sur S.A.", 1, "Ruta 8 km 225"),
        1, Today, null, null, null, [new ArcaVoucherOrderItem("Semilla de soja", 10m, 52000m), new ArcaVoucherOrderItem("Flete", 1.5m, 0.125m)], 520000.19m);
}
