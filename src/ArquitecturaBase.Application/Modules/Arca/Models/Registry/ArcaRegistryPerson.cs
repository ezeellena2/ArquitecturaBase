namespace ArquitecturaBase.Application.Modules.Arca.Models.Registry;

/// <summary>
/// Lo que trae <c>getPersona_v2</c> de una CUIT: la razón social o el nombre y apellido, el domicilio fiscal, los
/// impuestos del régimen general, si es monotributista y los errores que ARCA informa sobre la constancia.
/// </summary>
public sealed record ArcaRegistryPerson(string? LegalName, string? FirstName, string? LastName, ArcaRegistryAddress? FiscalAddress,
    IReadOnlyList<int> TaxIds, bool IsMonotax, IReadOnlyList<string> Errors)
{
    /// <summary>
    /// B3: los errores de los grupos que deciden la condición frente al IVA (régimen general y monotributo). Con alguno, la
    /// condición no se infiere: faltarían justo los datos que la deciden.
    /// </summary>
    public IReadOnlyList<string> VatConditionErrors { get; init; } = [];
}

public sealed record ArcaRegistryAddress(string? Street, string? Locality, string? Province, string? PostalCode);
