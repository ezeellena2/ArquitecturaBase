namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Un renglón como lo muestra el PDF fiscal: en la A, precio e importe sin IVA y la alícuota («21%», «Exento», «No gravado»);
/// en la B, con el IVA incluido y sin alícuota; en la C, como se cargó.
/// </summary>
public sealed record ArcaFiscalLine(string Description, decimal Quantity, decimal UnitPrice, decimal Amount, string? Vat = null);
