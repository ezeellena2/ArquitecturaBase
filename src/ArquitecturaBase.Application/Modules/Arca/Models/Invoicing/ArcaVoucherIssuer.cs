using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>El emisor como era al encolar: lo que muestra el PDF fiscal aunque después cambie el padrón o la configuración.</summary>
public sealed record ArcaVoucherIssuer(string LegalName, string FiscalAddress, ArcaTaxStatus TaxStatus, string? GrossIncomeNumber, DateOnly? ActivityStartOn);
