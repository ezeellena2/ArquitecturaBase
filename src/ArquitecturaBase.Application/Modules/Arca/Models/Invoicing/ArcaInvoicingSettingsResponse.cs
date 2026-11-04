namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>La configuración de Facturación: el punto de venta elegido, o null si todavía no se eligió.</summary>
public sealed record ArcaInvoicingSettingsResponse(int? PointOfSale);
