namespace ArquitecturaBase.Application.Modules.Arca.Models;

/// <summary>
/// The «Clientes» catalog the «Factura» brings, as the families that use it name it: its stable key and the keys of its fields.
/// The invoice reads them as the receiver; the registry completes them from the CUIT.
/// </summary>
public static class ArcaCustomersCatalog
{
    public const string Key = "arca.customers";
    public const string TaxId = "tax_id";
    public const string VatCondition = "vat_condition";
    public const string Address = "address";
    public const string Email = "email";
}
