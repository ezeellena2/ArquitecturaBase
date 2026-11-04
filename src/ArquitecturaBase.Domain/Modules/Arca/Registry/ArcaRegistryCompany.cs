using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.Arca.Registry;

/// <summary>Los datos de la empresa emisora que trajo el padrón en la última prueba de la conexión.</summary>
public sealed class ArcaRegistryCompany : Entity
{
    private ArcaRegistryCompany() { }

    public Guid ConnectionId { get; private set; }
    public string LegalName { get; private set; } = string.Empty;
    public ArcaVatCondition VatCondition { get; private set; }
    public string FiscalAddress { get; private set; } = string.Empty;

    public static ArcaRegistryCompany Create(Guid connectionId, string legalName, ArcaVatCondition vatCondition, string fiscalAddress)
    {
        var company = new ArcaRegistryCompany { ConnectionId = connectionId };
        company.Update(legalName, vatCondition, fiscalAddress);
        return company;
    }

    public void Update(string legalName, ArcaVatCondition vatCondition, string fiscalAddress)
    {
        LegalName = legalName.Trim();
        VatCondition = vatCondition;
        FiscalAddress = fiscalAddress.Trim();
    }
}
