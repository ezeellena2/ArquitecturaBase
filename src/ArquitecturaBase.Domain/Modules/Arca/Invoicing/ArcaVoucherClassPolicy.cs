using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// La clase del comprobante la decide el sistema, no la persona (tabla «Qué comprobante corresponde» del estudio): el
/// monotributo y el exento emiten C a cualquier receptor; el responsable inscripto, A o B según el receptor, que llega con la
/// etapa 4. La condición del receptor tiene que ser una de las de ARCA y admitir la clase.
/// </summary>
public static class ArcaVoucherClassPolicy
{
    public static Result<ArcaVoucherClass> Decide(ArcaTaxStatus issuer, int receiverCondition)
    {
        if (!ArcaReceiverVatConditions.Exists(receiverCondition)) return ArcaInvoicingErrors.ReceiverConditionInvalid;
        return issuer switch
        {
            ArcaTaxStatus.Monotax or ArcaTaxStatus.Exempt => ArcaReceiverVatConditions.Admits(receiverCondition, ArcaVoucherClass.C)
                ? ArcaVoucherClass.C
                : ArcaInvoicingErrors.ReceiverConditionInvalid,
            ArcaTaxStatus.RegisteredTaxpayer => ArcaInvoicingErrors.ClassNotSupported,
            _ => ArcaInvoicingErrors.IssuerNotRegistered,
        };
    }
}
