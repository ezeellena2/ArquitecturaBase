using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// La configuración propia de Facturación: el punto de venta exclusivo de la plataforma y los datos del comprobante que el
/// padrón no trae (ingresos brutos e inicio de actividades), que van en el PDF fiscal.
/// </summary>
public sealed class ArcaInvoicingSettings : Entity
{
    private const int GrossIncomeMaxLength = 30;

    private ArcaInvoicingSettings() { }

    public Guid ConnectionId { get; private set; }
    public int? PointOfSale { get; private set; }
    public string? GrossIncomeNumber { get; private set; }
    public DateOnly? ActivityStartOn { get; private set; }
    public bool IsComplete => PointOfSale is not null;
    public bool HasVoucherData => GrossIncomeNumber is not null && ActivityStartOn is not null;

    public static ArcaInvoicingSettings Create(Guid connectionId) => new() { ConnectionId = connectionId };

    public Result ChoosePointOfSale(int number)
    {
        if (number is < 1 or > 99998) return ArcaInvoicingErrors.PointOfSaleInvalid;
        PointOfSale = number;
        return Result.Success();
    }

    public Result SetVoucherData(string? grossIncomeNumber, DateOnly activityStartOn)
    {
        var grossIncome = grossIncomeNumber?.Trim();
        if (string.IsNullOrEmpty(grossIncome) || grossIncome.Length > GrossIncomeMaxLength) return ArcaInvoicingErrors.GrossIncomeInvalid;
        if (activityStartOn == default) return ArcaInvoicingErrors.ActivityStartInvalid;
        (GrossIncomeNumber, ActivityStartOn) = (grossIncome, activityStartOn);
        return Result.Success();
    }
}
