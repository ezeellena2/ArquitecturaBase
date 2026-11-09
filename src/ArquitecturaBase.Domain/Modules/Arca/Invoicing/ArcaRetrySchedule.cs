namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// La espera entre intentos de una emisión que no salió: crece con cada falla seguida hasta una hora y se queda ahí. No hay
/// tope de intentos: el límite es la ventana de la fecha del comprobante.
/// </summary>
public static class ArcaRetrySchedule
{
    private static readonly TimeSpan[] Steps =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
    ];

    public static TimeSpan After(int failures) => failures <= 0 ? TimeSpan.Zero : Steps[Math.Min(failures, Steps.Length) - 1];
}
