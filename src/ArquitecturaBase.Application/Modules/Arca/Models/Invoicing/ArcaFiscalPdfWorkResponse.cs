namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>El PDF fiscal que falta guardar: el comprobante, su clave en el almacén privado y lo que muestra.</summary>
public sealed record ArcaFiscalPdfWorkResponse(Guid VoucherId, string StorageKey, ArcaFiscalDocument Document);
