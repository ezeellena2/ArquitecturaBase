using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// La reconciliación del ADR 0028, con el ADR 0031: coincide si ARCA tiene el mismo documento del receptor, fecha, concepto e
/// importe total. Si ARCA no lo tiene y el número sigue siendo el siguiente, se vuelve a pedir con el mismo número, salvo que
/// ARCA no pueda aceptar su fecha: anterior a la del último comprobante autorizado, o fuera de la ventana. Entonces, y en lo
/// demás que no es «se emitió», decide una persona. Un 10016 solo no alcanza para soltar el número: puede ser nuestro, autorizado
/// tarde y todavía sin mostrarse.
/// </summary>
internal static class ArcaReconciliationPolicy
{
    public static ArcaReconciliation Decide(ArcaVoucherRequest sent, ArcaIssuedVoucher? found, long lastAuthorized, DateOnly? lastIssuedOn, DateOnly today)
    {
        if (found is not null) return Matches(sent, found) ? ArcaReconciliation.Issued : ArcaReconciliation.Taken;
        if (lastAuthorized >= sent.Number) return ArcaReconciliation.Missing;
        return sent.IssuedOn < lastIssuedOn || !ArcaIssueDateWindow.Admits(sent.Concept, sent.IssuedOn, today)
            ? ArcaReconciliation.DateRefused : ArcaReconciliation.NotIssued;
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
