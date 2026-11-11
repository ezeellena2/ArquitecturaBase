namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// Lo que el padrón sabe de un tercero por su CUIT, para las familias que emiten: el nombre (la razón social, o el nombre y
/// apellido), la condición frente al IVA como el código del receptor de WSFEv1 y el domicilio fiscal, si lo tiene.
/// </summary>
public sealed record ArcaParty(string Cuit, string LegalName, int VatConditionId, string? FiscalAddress)
{
    public override string ToString() => "ArcaParty { Content = [REDACTED] }";
}
