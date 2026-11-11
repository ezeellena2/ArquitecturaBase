namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>El punto de venta que se elige y la revisión de la configuración de Facturación que se leyó.</summary>
public sealed record ChooseArcaPointOfSaleRequest(int PointOfSale, long ExpectedRevision);
