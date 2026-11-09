namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// La fecha que ARCA acepta en <c>CbteFch</c> (manual 4.7): con concepto 1 (productos), hasta 5 días antes o después de la
/// fecha de generación y sin pasar al mes siguiente; con concepto 2 o 3 (servicios), hasta 10 días. Las fechas son de la
/// Argentina, que está en UTC−3 todo el año.
/// </summary>
public static class ArcaIssueDateWindow
{
    private static readonly TimeSpan ArgentinaOffset = TimeSpan.FromHours(-3);

    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(utcNow + ArgentinaOffset);

    public static bool Admits(int concept, DateOnly date, DateOnly today)
    {
        var days = Math.Abs(date.DayNumber - today.DayNumber);
        if (concept != 1) return days <= 10;
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        return days <= 5 && date < nextMonth;
    }
}
