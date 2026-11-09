namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// El receptor del comprobante: tipo y número de documento (80 CUIT, 96 DNI, 99 sin identificar con número 0), nombre,
/// condición frente al IVA (código de <c>FEParamGetCondicionIvaReceptor</c>) y domicilio.
/// </summary>
public sealed record ArcaVoucherParty(int DocumentType, long DocumentNumber, string Name, int VatConditionId, string? Address)
{
    public override string ToString() => $"ArcaVoucherParty {{ documentType: {DocumentType}, content: [REDACTED] }}";
}
