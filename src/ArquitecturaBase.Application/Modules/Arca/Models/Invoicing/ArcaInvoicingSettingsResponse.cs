namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// La configuración de Facturación: el punto de venta elegido y los datos del comprobante que el padrón no trae
/// (ingresos brutos e inicio de actividades), cada uno null mientras no se cargó.
/// </summary>
public sealed record ArcaInvoicingSettingsResponse(int? PointOfSale, string? GrossIncomeNumber = null, DateOnly? ActivityStartOn = null);
