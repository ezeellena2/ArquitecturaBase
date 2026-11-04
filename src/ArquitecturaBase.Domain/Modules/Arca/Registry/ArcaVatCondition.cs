namespace ArquitecturaBase.Domain.Modules.Arca.Registry;

/// <summary>La condición frente al IVA que sale del padrón; define si la empresa factura C o A y B.</summary>
public enum ArcaVatCondition
{
    RegisteredTaxpayer,
    Monotax,
    Exempt,
    NotRegistered,
}
