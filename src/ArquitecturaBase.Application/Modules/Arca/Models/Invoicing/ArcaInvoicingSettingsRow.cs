namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

public sealed record ArcaInvoicingSettingsRow(int? PointOfSale, string? GrossIncomeNumber = null, DateOnly? ActivityStartOn = null, long Revision = 0)
{
    public override string ToString() =>
        $"ArcaInvoicingSettingsRow {{ pointOfSale: {PointOfSale}, grossIncomeNumber: {(GrossIncomeNumber is null ? "null" : "[REDACTED]")}, revision: {Revision} }}";
}
