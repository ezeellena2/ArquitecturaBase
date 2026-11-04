namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Un punto de venta como lo informa <c>FEParamGetPtosVenta</c>: número, tipo de emisión, si está bloqueado y su baja.</summary>
public sealed record ArcaPointOfSale(int Number, string EmissionType, bool Blocked, DateOnly? DroppedOn);
