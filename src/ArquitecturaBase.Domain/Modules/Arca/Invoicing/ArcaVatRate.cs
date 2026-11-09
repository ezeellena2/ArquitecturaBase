namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// Una alícuota de IVA de ARCA: su código de <c>FEParamGetTiposIva</c> (3 es 0 %, 4 es 10,5 %, 5 es 21 %…) y su porcentaje.
/// Llega del catálogo de ARCA; un código o un porcentaje imposibles son un error del sistema.
/// </summary>
public sealed record ArcaVatRate
{
    public ArcaVatRate(int id, decimal percent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentOutOfRangeException.ThrowIfNegative(percent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100m);
        Id = id;
        Percent = percent;
    }

    public int Id { get; }
    public decimal Percent { get; }
}
