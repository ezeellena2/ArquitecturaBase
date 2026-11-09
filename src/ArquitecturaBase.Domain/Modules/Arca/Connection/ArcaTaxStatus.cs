namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// La condición frente al IVA de la empresa que emite, como la ven las familias que no son el padrón: define la clase de
/// comprobante (C para monotributo o exento; A o B para responsable inscripto). La traduce el padrón desde su constancia.
/// </summary>
public enum ArcaTaxStatus
{
    RegisteredTaxpayer,
    Monotax,
    Exempt,
    NotRegistered,
}
