using ArquitecturaBase.Domain.Modules.Arca.Registry;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Registry;

public sealed class ArcaRegistryCompanyTests
{
    [Fact]
    public void The_registry_data_is_kept_trimmed_for_its_connection()
    {
        var connectionId = Guid.CreateVersion7();

        var company = ArcaRegistryCompany.Create(connectionId, "  Agropecuaria Los Tilos S.A. ", ArcaVatCondition.RegisteredTaxpayer, " Ruta 8 km 612 ");

        Assert.Equal(connectionId, company.ConnectionId);
        Assert.Equal("Agropecuaria Los Tilos S.A.", company.LegalName);
        Assert.Equal(ArcaVatCondition.RegisteredTaxpayer, company.VatCondition);
        Assert.Equal("Ruta 8 km 612", company.FiscalAddress);
    }

    [Fact]
    public void A_new_reading_replaces_the_previous_one()
    {
        var company = ArcaRegistryCompany.Create(Guid.CreateVersion7(), "Los Tilos", ArcaVatCondition.Monotax, "Calle 1");

        company.Update("Los Tilos S.A.", ArcaVatCondition.RegisteredTaxpayer, "Ruta 8");

        Assert.Equal("Los Tilos S.A.", company.LegalName);
        Assert.Equal(ArcaVatCondition.RegisteredTaxpayer, company.VatCondition);
        Assert.Equal("Ruta 8", company.FiscalAddress);
    }
}
