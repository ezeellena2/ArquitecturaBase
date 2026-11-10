namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Un importe del pie del PDF fiscal de la A, antes del total: el neto gravado, el IVA de cada alícuota, lo exento.</summary>
public sealed record ArcaFiscalAmount(string Label, decimal Amount);
