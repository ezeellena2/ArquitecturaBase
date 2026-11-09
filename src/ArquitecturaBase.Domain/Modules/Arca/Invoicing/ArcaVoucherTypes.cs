namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>El código de <c>CbteTipo</c> de cada clase y tipo de comprobante (tabla de <c>FEParamGetTiposCbte</c>).</summary>
public static class ArcaVoucherTypes
{
    private static readonly Dictionary<(ArcaVoucherClass, ArcaVoucherKind), int> Codes = new()
    {
        [(ArcaVoucherClass.A, ArcaVoucherKind.Invoice)] = 1,
        [(ArcaVoucherClass.A, ArcaVoucherKind.CreditNote)] = 3,
        [(ArcaVoucherClass.B, ArcaVoucherKind.Invoice)] = 6,
        [(ArcaVoucherClass.B, ArcaVoucherKind.CreditNote)] = 8,
        [(ArcaVoucherClass.C, ArcaVoucherKind.Invoice)] = 11,
        [(ArcaVoucherClass.C, ArcaVoucherKind.CreditNote)] = 13,
    };

    public static int Code(ArcaVoucherClass voucherClass, ArcaVoucherKind kind) => Codes[(voucherClass, kind)];
}
