namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>La clase de comprobante: A y B discriminan IVA (responsable inscripto); C no (monotributo o exento).</summary>
public enum ArcaVoucherClass
{
    A,
    B,
    C,
}
