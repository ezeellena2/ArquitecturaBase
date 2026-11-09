namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Lo que ARCA tiene registrado de un comprobante (<c>FECompConsultar</c>): lo que se mandó y el código de autorización.
/// Alcanza para reconciliar: documento del receptor, fecha, concepto e importe total.
/// </summary>
public sealed record ArcaIssuedVoucher(int Concept, int DocumentType, long DocumentNumber, DateOnly IssuedOn, decimal Total, string? Result,
    string? AuthorizationCode, DateOnly? AuthorizationDueOn)
{
    public override string ToString() => $"ArcaIssuedVoucher {{ result: {Result}, document: [REDACTED] }}";
}
