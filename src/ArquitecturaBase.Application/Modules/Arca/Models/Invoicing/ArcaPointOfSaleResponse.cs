namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Un punto de venta que se puede mostrar: los dados de baja no vienen y los bloqueados se ven apagados.</summary>
public sealed record ArcaPointOfSaleResponse(int Number, string EmissionType, bool Blocked);
