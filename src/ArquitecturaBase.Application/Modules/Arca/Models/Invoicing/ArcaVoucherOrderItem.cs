namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Un renglón pedido: descripción, cantidad, precio unitario y, en la factura A o B, su IVA.</summary>
public sealed record ArcaVoucherOrderItem(string Description, decimal Quantity, decimal UnitPrice, ArcaVoucherVat? Vat = null);
