using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Channels.Registry;

/// <summary>El padrón presenta los datos de la empresa a las familias que emiten, con su condición frente al IVA.</summary>
internal sealed class ArcaRegistryCompanyProfileSource(IArcaRegistryReader reader) : IArcaCompanyProfileSource
{
    public async Task<ArcaCompanyProfile?> FindAsync(Guid connectionId, CancellationToken cancellationToken) =>
        await reader.FindCompanyAsync(connectionId, cancellationToken) is { } company
            ? new ArcaCompanyProfile(company.LegalName, company.FiscalAddress, company.VatCondition switch
            {
                ArcaVatCondition.RegisteredTaxpayer => ArcaTaxStatus.RegisteredTaxpayer,
                ArcaVatCondition.Monotax => ArcaTaxStatus.Monotax,
                ArcaVatCondition.Exempt => ArcaTaxStatus.Exempt,
                _ => ArcaTaxStatus.NotRegistered,
            })
            : null;
}
