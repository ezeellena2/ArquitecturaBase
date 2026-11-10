using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaInvoiceOrderPolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public void A_customer_with_a_cuit_becomes_a_receiver_with_its_condition_and_the_run_is_the_request_key()
    {
        var order = ArcaInvoiceOrderPolicy.Order(Request(Customer("20-12345678-6", "6", "Ruta 9 km 400")), Today).Value;

        Assert.Equal(RunId, order.RequestKey);
        Assert.Equal((80, 20123456786L, "Juana Pérez", 6, "Ruta 9 km 400"),
            (order.Receiver.DocumentType, order.Receiver.DocumentNumber, order.Receiver.Name, order.Receiver.VatConditionId, order.Receiver.Address));
        Assert.Equal(2, order.Concept);
        Assert.Equal(new DateOnly(2026, 11, 8), order.IssueOn);
        Assert.Equal((new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), new DateOnly(2026, 12, 10)), (order.ServiceFrom, order.ServiceTo, order.PaymentDueOn));
        Assert.Equal([("Horas de consultoría", 2m, 1500.5m), ("Viático", 1m, 100m)], order.Items.Select(item => (item.Description, item.Quantity, item.UnitPrice)));
        Assert.All(order.Items, item => Assert.Null(item.Vat));
        Assert.Equal(3101m, order.ExpectedTotal);
    }

    [Fact]
    public void A_customer_without_a_cuit_is_a_final_consumer()
    {
        var order = ArcaInvoiceOrderPolicy.Order(Request(new StructuredIssueDatum("customer", "c1", "Consumidor final")), Today).Value;

        Assert.Equal((99, 0L, 5), (order.Receiver.DocumentType, order.Receiver.DocumentNumber, order.Receiver.VatConditionId));
    }

    [Fact]
    public void Without_a_date_it_is_issued_today_and_without_a_total_the_server_does_not_compare()
    {
        var data = Data(Customer("20123456786", "1", null)).Where(datum => datum.Key is not ("issueDate" or "total")).ToArray();

        var order = ArcaInvoiceOrderPolicy.Order(new StructuredIssueRequest(RunId, Guid.NewGuid(), data), Today).Value;

        Assert.Equal(Today, order.IssueOn);
        Assert.Null(order.ExpectedTotal);
    }

    [Fact]
    public void Missing_or_unreadable_data_is_refused_with_its_arca_error()
    {
        Assert.Equal(ArcaInvoicingErrors.ReceiverInvalid, Order(data => data.Where(datum => datum.Key != "customer")).Error);
        Assert.Equal(ArcaInvoicingErrors.ReceiverConditionInvalid, ArcaInvoiceOrderPolicy.Order(Request(Customer("20123456786", null, null)), Today).Error);
        Assert.Equal(ArcaInvoicingErrors.ConceptInvalid, Order(data => data.Where(datum => datum.Key != "concept")).Error);
        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, Order(data => data.Where(datum => datum.Key != "items")).Error);
        Assert.Equal(ArcaInvoicingErrors.ItemsInvalid, Order(data => data.Select(datum => datum.Key == "items"
            ? datum with { Items = [[new StructuredIssueDatum("description", "Sin precio"), new StructuredIssueDatum("quantity", "1")]] } : datum)).Error);
    }

    private static Result<ArcaVoucherOrder> Order(Func<IEnumerable<StructuredIssueDatum>, IEnumerable<StructuredIssueDatum>> change) =>
        ArcaInvoiceOrderPolicy.Order(new StructuredIssueRequest(RunId, Guid.NewGuid(), change(Data(Customer("20123456786", "6", null))).ToArray()), Today);

    private static StructuredIssueRequest Request(StructuredIssueDatum customer) => new(RunId, Guid.NewGuid(), Data(customer));

    private static StructuredIssueDatum Customer(string? cuit, string? condition, string? address)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        if (cuit is not null) fields["tax_id"] = cuit;
        if (condition is not null) fields["vat_condition"] = condition;
        if (address is not null) fields["address"] = address;
        return new StructuredIssueDatum("customer", Guid.NewGuid().ToString("D"), "Juana Pérez", fields);
    }

    private static StructuredIssueDatum[] Data(StructuredIssueDatum customer) =>
    [
        customer,
        new("concept", "2", "Servicios"),
        new("issueDate", "2026-11-08"),
        new("serviceFrom", "2026-11-01"),
        new("serviceTo", "2026-11-30"),
        new("paymentDue", "2026-12-10"),
        new("items", null, Items:
        [
            [new("description", "Horas de consultoría"), new("quantity", "2"), new("unitPrice", "1500.5"), new("amount", "3001")],
            [new("description", "Viático"), new("quantity", "1"), new("unitPrice", "100"), new("amount", "100")],
        ]),
        new("total", "3101"),
    ];
}
