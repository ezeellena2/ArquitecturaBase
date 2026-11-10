namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// The keys of the «Factura» standard: the issuer, the datum keys its bound questions carry and the field keys of the «Clientes»
/// catalog. The standard (task 11) and the issuer read the same keys.
/// </summary>
internal static class ArcaInvoiceStandard
{
    public const string Key = "arca.invoice";
    public const string Customer = "customer", Concept = "concept", IssueDate = "issueDate", ServiceFrom = "serviceFrom",
        ServiceTo = "serviceTo", PaymentDue = "paymentDue", Items = "items", Description = "description", Quantity = "quantity",
        UnitPrice = "unitPrice", Total = "total";
    public const string TaxIdField = "tax_id", VatConditionField = "vat_condition", AddressField = "address";
}
