namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// Un renglón del comprobante: cantidad y precio unitario. Su importe es el producto redondeado a 2 decimales con half-even
/// (el redondeo de WSFEv1); en la clase C, el neto y el total son la suma de los importes. La carga ya rechaza una cantidad o
/// un precio inválidos: acá son un error del sistema.
/// </summary>
public readonly record struct ArcaInvoiceLine(decimal Quantity, decimal UnitPrice)
{
    public static decimal Amount(decimal quantity, decimal unitPrice)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        ArgumentOutOfRangeException.ThrowIfNegative(unitPrice);
        return Math.Round(quantity * unitPrice, 2, MidpointRounding.ToEven);
    }

    public static decimal Total(IEnumerable<ArcaInvoiceLine> lines) => lines.Sum(line => Amount(line.Quantity, line.UnitPrice));
}
