namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// Las condiciones frente al IVA del receptor (<c>CondicionIVAReceptorId</c>, RG 5616) y las clases que admite cada una,
/// según el anexo del manual de WSFEv1 (4.7). La tabla que devuelve <c>FEParamGetCondicionIvaReceptor</c> llega además
/// como catálogo; ésta valida antes de encolar y ARCA vuelve a controlar al emitir (10243).
/// </summary>
public static class ArcaReceiverVatConditions
{
    /// <summary>Consumidor final: el receptor sin CUIT.</summary>
    public const int FinalConsumer = 5;

    private static readonly ArcaVoucherClass[] AandC = [ArcaVoucherClass.A, ArcaVoucherClass.C];
    private static readonly ArcaVoucherClass[] BandC = [ArcaVoucherClass.B, ArcaVoucherClass.C];

    private static readonly Dictionary<int, ArcaVoucherClass[]> Classes = new()
    {
        [1] = AandC, [4] = BandC, [5] = BandC, [6] = AandC, [7] = BandC, [8] = BandC, [9] = BandC, [10] = BandC, [13] = AandC, [15] = BandC,
        [16] = AandC,
    };

    public static bool Exists(int condition) => Classes.ContainsKey(condition);

    public static bool Admits(int condition, ArcaVoucherClass voucherClass) =>
        Classes.TryGetValue(condition, out var classes) && classes.Contains(voucherClass);
}
