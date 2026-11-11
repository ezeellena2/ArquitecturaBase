using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// La configuración propia de Facturación: el punto de venta exclusivo de la plataforma y los datos del comprobante que el
/// padrón no trae (ingresos brutos e inicio de actividades), que van en el PDF fiscal. Es configuración interna (ADR 0018):
/// cada cambio guardado avanza la revisión, y quien guarda trae la que leyó.
/// </summary>
public sealed class ArcaInvoicingSettings : Entity
{
    private const int GrossIncomeMaxLength = 30;

    private ArcaInvoicingSettings() { }

    public Guid ConnectionId { get; private set; }
    public int? PointOfSale { get; private set; }
    public string? GrossIncomeNumber { get; private set; }
    public DateOnly? ActivityStartOn { get; private set; }
    public long Revision { get; private set; }
    public bool IsComplete => PointOfSale is not null;
    public bool HasVoucherData => GrossIncomeNumber is not null && ActivityStartOn is not null;

    public static ArcaInvoicingSettings Create(Guid connectionId) => new() { ConnectionId = connectionId };

    public Result ChoosePointOfSale(int number)
    {
        if (number is < 1 or > 99998) return ArcaInvoicingErrors.PointOfSaleInvalid;
        PointOfSale = number;
        Advance();
        return Result.Success();
    }

    public Result SetVoucherData(string? grossIncomeNumber, DateOnly activityStartOn)
    {
        var grossIncome = grossIncomeNumber?.Trim();
        if (string.IsNullOrEmpty(grossIncome) || grossIncome.Length > GrossIncomeMaxLength) return ArcaInvoicingErrors.GrossIncomeInvalid;
        if (activityStartOn == default) return ArcaInvoicingErrors.ActivityStartInvalid;
        (GrossIncomeNumber, ActivityStartOn) = (grossIncome, activityStartOn);
        Advance();
        return Result.Success();
    }

    /// <summary>Otro guardó desde que se leyó: sin la configuración todavía, la revisión leída es 0.</summary>
    public Result EnsureRevision(long expectedRevision) =>
        expectedRevision == Revision ? Result.Success() : ArcaInvoicingErrors.SettingsRevisionConflict;

    private void Advance() => Revision = checked(Revision + 1);
}
