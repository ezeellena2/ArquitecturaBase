using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Application.Modules.Arca.Services.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Registry;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Registry;

public sealed class ArcaCompanyDataPolicyTests
{
    private static readonly ArcaRegistryAddress Address = new("Ruta 9 km 400", "Marcos Juárez", "CORDOBA", "2580");

    [Fact]
    public void Monotax_data_makes_a_monotax_company_even_with_other_taxes()
    {
        Assert.Equal(ArcaVatCondition.Monotax, ArcaCompanyDataPolicy.VatCondition(Person(taxIds: [30], isMonotax: true)));
    }

    [Fact]
    public void The_vat_tax_makes_a_registered_taxpayer()
    {
        Assert.Equal(ArcaVatCondition.RegisteredTaxpayer, ArcaCompanyDataPolicy.VatCondition(Person(taxIds: [10, 30])));
    }

    [Fact]
    public void The_exempt_vat_tax_makes_an_exempt_company()
    {
        Assert.Equal(ArcaVatCondition.Exempt, ArcaCompanyDataPolicy.VatCondition(Person(taxIds: [32])));
    }

    [Fact]
    public void Without_vat_or_monotax_the_company_is_not_registered()
    {
        Assert.Equal(ArcaVatCondition.NotRegistered, ArcaCompanyDataPolicy.VatCondition(Person(taxIds: [10])));
    }

    [Theory]
    [InlineData(true, new[] { 30 }, 6)]
    [InlineData(false, new[] { 10, 30 }, 1)]
    [InlineData(false, new[] { 32 }, 4)]
    [InlineData(false, new[] { 10 }, 5)]
    public void A_third_party_gets_the_receiver_condition_code_of_arca(bool isMonotax, int[] taxIds, int expected)
    {
        Assert.Equal(expected, ArcaCompanyDataPolicy.ReceiverVatCondition(Person(taxIds: taxIds, isMonotax: isMonotax)));
    }

    [Fact]
    public void The_fiscal_address_is_street_locality_and_province()
    {
        Assert.Equal("Ruta 9 km 400, Marcos Juárez, CORDOBA", ArcaCompanyDataPolicy.FiscalAddress(Person()));
        Assert.Equal("Ruta 9 km 400, CORDOBA", ArcaCompanyDataPolicy.FiscalAddress(Person(address: Address with { Locality = " " })));
        Assert.Equal(string.Empty, ArcaCompanyDataPolicy.FiscalAddress(new ArcaRegistryPerson("Campo SA", null, null, null, [], false, [])));
    }

    [Fact]
    public void The_legal_name_is_the_business_name_or_the_first_and_last_name()
    {
        Assert.Equal("Campo SA", ArcaCompanyDataPolicy.LegalName(Person(legalName: " Campo SA ")));
        Assert.Equal("Juana Pérez", ArcaCompanyDataPolicy.LegalName(Person(legalName: null, firstName: "Juana", lastName: "Pérez")));
        Assert.Equal(string.Empty, ArcaCompanyDataPolicy.LegalName(Person(legalName: null)));
    }

    private static ArcaRegistryPerson Person(string? legalName = "Campo SA", string? firstName = null, string? lastName = null,
        ArcaRegistryAddress? address = null, int[]? taxIds = null, bool isMonotax = false) =>
        new(legalName, firstName, lastName, address ?? Address, taxIds ?? [], isMonotax, []);
}
