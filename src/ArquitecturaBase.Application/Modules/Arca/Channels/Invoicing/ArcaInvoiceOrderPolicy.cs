using System.Globalization;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;
using ArquitecturaBase.Domain.Results;
using static ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing.ArcaInvoiceStandard;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;

/// <summary>
/// The order of a response copied from «Factura»: the customer of «Clientes» (with its CUIT, a receiver with its condition; without
/// it, a final consumer), the concept by its ARCA code, the dates and the items, each with its VAT for classes A and B, and whether
/// the form's prices include VAT. It only reads; the queue validates the order and decides whether the VAT is needed.
/// </summary>
internal static class ArcaInvoiceOrderPolicy
{
    private const int CuitDocument = 80, UnidentifiedDocument = 99;

    public static Result<ArcaVoucherOrder> Order(StructuredIssueRequest request, DateOnly today)
    {
        var data = request.Data.ToDictionary(datum => datum.Key, StringComparer.Ordinal);
        if (!data.TryGetValue(Customer, out var customer) || string.IsNullOrWhiteSpace(customer.Label)) return ArcaInvoicingErrors.ReceiverInvalid;
        var fields = customer.Fields ?? new Dictionary<string, string>();
        ArcaVoucherParty receiver;
        if (fields.TryGetValue(TaxIdField, out var taxId) && !string.IsNullOrWhiteSpace(taxId))
        {
            if (!long.TryParse(new string(taxId.Where(char.IsAsciiDigit).ToArray()), NumberStyles.None, CultureInfo.InvariantCulture, out var cuit))
                return ArcaInvoicingErrors.ReceiverInvalid;
            if (!fields.TryGetValue(VatConditionField, out var condition) || !int.TryParse(condition, NumberStyles.None, CultureInfo.InvariantCulture, out var conditionId))
                return ArcaInvoicingErrors.ReceiverConditionInvalid;
            receiver = new(CuitDocument, cuit, customer.Label.Trim(), conditionId, Address(fields));
        }
        else
            receiver = new(UnidentifiedDocument, 0, customer.Label.Trim(), ArcaReceiverVatConditions.FinalConsumer, Address(fields));

        if (!data.TryGetValue(Concept, out var concept) || !int.TryParse(concept.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var conceptId))
            return ArcaInvoicingErrors.ConceptInvalid;
        if (!data.TryGetValue(Items, out var items) || items.Items is not { Count: > 0 }) return ArcaInvoicingErrors.ItemsInvalid;
        var lines = new List<ArcaVoucherOrderItem>();
        foreach (var item in items.Items)
        {
            var values = item.ToDictionary(datum => datum.Key, datum => datum.Value, StringComparer.Ordinal);
            if (values.GetValueOrDefault(Description) is not { } description || Number(values.GetValueOrDefault(Quantity)) is not { } quantity
                || Number(values.GetValueOrDefault(UnitPrice)) is not { } unitPrice)
                return ArcaInvoicingErrors.ItemsInvalid;
            ArcaVoucherVat? vat = null;
            if (values.GetValueOrDefault(VatRate) is { } rate && (vat = Vat(rate)) is null) return ArcaInvoicingErrors.ItemsInvalid;
            lines.Add(new(description, quantity, unitPrice, vat));
        }
        return new ArcaVoucherOrder(request.RunId, receiver, conceptId, Date(data, IssueDate) ?? today, Date(data, ServiceFrom), Date(data, ServiceTo),
            Date(data, PaymentDue), lines, Number(data.GetValueOrDefault(Total)?.Value), data.GetValueOrDefault(PricesIncludeVat)?.Value == "true");
    }

    private static ArcaVoucherVat? Vat(string value) => value switch
    {
        Exempt => new(ArcaVatTreatmentKind.Exempt),
        NotTaxed => new(ArcaVatTreatmentKind.NotTaxed),
        _ => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var code) ? new(ArcaVatTreatmentKind.Taxed, code) : null,
    };

    private static string? Address(IReadOnlyDictionary<string, string> fields) =>
        fields.TryGetValue(AddressField, out var address) && !string.IsNullOrWhiteSpace(address) ? address.Trim() : null;

    private static DateOnly? Date(Dictionary<string, StructuredIssueDatum> data, string key) =>
        DateOnly.TryParseExact(data.GetValueOrDefault(key)?.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static decimal? Number(string? value) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) ? number : null;
}
