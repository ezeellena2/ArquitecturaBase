namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// Las alícuotas de IVA de ARCA (<c>FEParamGetTiposIva</c>): el servidor conoce el porcentaje de cada código y no lo toma de
/// quien pide. El manual de WSFEv1 confirma 3, 4, 5 y 6; 8 y 9 salen de la tabla de ARCA. Una alícuota nueva de ARCA se suma
/// acá.
/// </summary>
public static class ArcaVatRates
{
    private static readonly Dictionary<int, ArcaVatRate> Rates = new ArcaVatRate[]
    {
        new(3, 0m), new(4, 10.5m), new(5, 21m), new(6, 27m), new(8, 5m), new(9, 2.5m),
    }.ToDictionary(rate => rate.Id);

    public static ArcaVatRate? Find(int id) => Rates.GetValueOrDefault(id);
}
