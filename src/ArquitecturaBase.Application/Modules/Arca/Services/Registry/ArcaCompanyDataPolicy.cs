using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Modules.Arca.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Registry;

/// <summary>
/// Los datos de la empresa emisora a partir del padrón. La condición frente al IVA sale de los impuestos: el monotributo
/// manda; si no, el impuesto 30 (IVA) es responsable inscripto y el 32 (IVA exento), exento.
/// </summary>
internal static class ArcaCompanyDataPolicy
{
    private const int VatTaxId = 30;
    private const int ExemptVatTaxId = 32;

    public static bool HasData(ArcaRegistryPerson person) => LegalName(person).Length > 0;

    public static string LegalName(ArcaRegistryPerson person) =>
        string.IsNullOrWhiteSpace(person.LegalName) ? Join(" ", person.FirstName, person.LastName) : person.LegalName.Trim();

    public static ArcaVatCondition VatCondition(ArcaRegistryPerson person) =>
        person.IsMonotax ? ArcaVatCondition.Monotax
        : person.TaxIds.Contains(VatTaxId) ? ArcaVatCondition.RegisteredTaxpayer
        : person.TaxIds.Contains(ExemptVatTaxId) ? ArcaVatCondition.Exempt
        : ArcaVatCondition.NotRegistered;

    public static string FiscalAddress(ArcaRegistryPerson person) =>
        person.FiscalAddress is { } address ? Join(", ", address.Street, address.Locality, address.Province) : string.Empty;

    private static string Join(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
}
