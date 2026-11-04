using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaInvoicingErrors
{
    public static readonly Error PointOfSaleInvalid = Error.Validation("Arca.Invoicing.PointOfSaleInvalid", "The point of sale must be between 1 and 99998.");
    public static readonly Error PointOfSaleUnavailable = Error.Validation("Arca.Invoicing.PointOfSaleUnavailable", "ARCA no longer offers that point of sale.");
}
