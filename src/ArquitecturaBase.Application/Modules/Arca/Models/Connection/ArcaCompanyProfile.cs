using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>Los datos de la empresa que emite, como los trajo el padrón en la última prueba de la conexión.</summary>
public sealed record ArcaCompanyProfile(string LegalName, string FiscalAddress, ArcaTaxStatus TaxStatus);
