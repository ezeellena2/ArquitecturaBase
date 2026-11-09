namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Un renglón pedido: descripción, cantidad y precio unitario.</summary>
public sealed record ArcaVoucherOrderItem(string Description, decimal Quantity, decimal UnitPrice);
