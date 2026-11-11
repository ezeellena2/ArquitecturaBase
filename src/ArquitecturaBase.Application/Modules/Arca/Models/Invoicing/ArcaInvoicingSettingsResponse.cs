namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// La configuración de Facturación: el punto de venta elegido y los datos del comprobante que el padrón no trae
/// (ingresos brutos e inicio de actividades), cada uno null mientras no se cargó, y la revisión que se manda al guardar
/// (0 mientras no se guardó nada). El número de ingresos brutos no se imprime.
/// </summary>
public sealed record ArcaInvoicingSettingsResponse(int? PointOfSale, string? GrossIncomeNumber, DateOnly? ActivityStartOn, long Revision)
{
    public override string ToString() =>
        $"ArcaInvoicingSettingsResponse {{ pointOfSale: {PointOfSale}, grossIncomeNumber: {(GrossIncomeNumber is null ? "null" : "[REDACTED]")}, revision: {Revision} }}";
}
