using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// La reconciliación del ADR 0028: coincide si ARCA tiene el mismo documento del receptor, fecha, concepto e importe total.
/// Sólo «no se emitió» habilita volver a pedir, con el mismo número; lo demás que no es «se emitió» lo decide una persona.
/// </summary>
internal static class ArcaReconciliationPolicy
{
    public static ArcaReconciliation Decide(ArcaVoucherRequest sent, ArcaIssuedVoucher? found, long lastAuthorized)
    {
        if (found is not null) return Matches(sent, found) ? ArcaReconciliation.Issued : ArcaReconciliation.Taken;
        return lastAuthorized < sent.Number ? ArcaReconciliation.NotIssued : ArcaReconciliation.Missing;
    }

    private static bool Matches(ArcaVoucherRequest sent, ArcaIssuedVoucher found) =>
        found.Result is null or "A"
        && found.AuthorizationCode is not null
        && found.Concept == sent.Concept
        && found.DocumentType == sent.DocumentType
        && found.DocumentNumber == sent.DocumentNumber
        && found.IssuedOn == sent.IssuedOn
        && found.Total == sent.Amounts.Total;
}
