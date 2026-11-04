using ArquitecturaBase.Domain.Modules.Arca.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Registry;

/// <summary>Los datos de la empresa que trajo el padrón; todo null hasta la primera prueba en verde.</summary>
public sealed record ArcaCompanyResponse(string? LegalName, ArcaVatCondition? VatCondition, string? FiscalAddress);
