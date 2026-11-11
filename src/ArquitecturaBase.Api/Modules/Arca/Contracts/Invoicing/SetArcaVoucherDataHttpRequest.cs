namespace ArquitecturaBase.Api.Modules.Arca.Contracts.Invoicing;

/// <summary>Los datos del comprobante que el padrón no trae: el número de ingresos brutos y el inicio de actividades.</summary>
public sealed record SetArcaVoucherDataHttpRequest(string? GrossIncomeNumber, DateOnly ActivityStartOn);
