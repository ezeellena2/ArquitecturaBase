namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// The «Clientes» catalog the «Factura» brings, as the families that use it name it: its stable key and the keys of its fields.
/// The invoice reads them as the receiver; the registry completes them from the CUIT. LegalName holds the whole name: the text of an
/// option admits 30 characters, and the invoice takes the receiver's name from this field when it has one. Initial (B5) is the
/// first letter of the name, what «Cliente» chooses first by: WhatsApp offers up to 200 options to choose one.
/// </summary>
public static class ArcaCustomersCatalog
{
    public const string Key = "arca.customers";
    public const string TaxId = "tax_id";
    public const string LegalName = "legal_name";
    public const string VatCondition = "vat_condition";
    public const string Address = "address";
    public const string Email = "email";
    public const string Initial = "initial";
}
