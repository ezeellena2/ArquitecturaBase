namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Invoicing;

/// <summary>El punto de venta que se elige y la revisión de la configuración de Facturación que se leyó (0 si no había).</summary>
public sealed record ChooseArcaPointOfSaleHttpRequest(int PointOfSale, long ExpectedRevision);
