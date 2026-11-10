namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Un renglón fijado: descripción, cantidad, precio unitario, importe redondeado con half-even y, en la factura A o B, su IVA.
/// </summary>
public sealed record ArcaVoucherLine(string Description, decimal Quantity, decimal UnitPrice, decimal Amount, ArcaVoucherVat? Vat = null);
