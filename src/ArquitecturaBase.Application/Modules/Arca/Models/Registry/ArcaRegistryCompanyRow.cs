using ArquitecturaBase.Domain.Modules.Arca.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Registry;

public sealed record ArcaRegistryCompanyRow(string LegalName, ArcaVatCondition VatCondition, string FiscalAddress);
