namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Un renglón fijado: descripción, cantidad, precio unitario e importe redondeado con half-even.</summary>
public sealed record ArcaVoucherLine(string Description, decimal Quantity, decimal UnitPrice, decimal Amount);
