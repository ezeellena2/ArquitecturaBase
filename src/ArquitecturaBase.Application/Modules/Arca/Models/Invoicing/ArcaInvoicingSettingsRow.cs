namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

public sealed record ArcaInvoicingSettingsRow(int? PointOfSale, string? GrossIncomeNumber = null, DateOnly? ActivityStartOn = null);
