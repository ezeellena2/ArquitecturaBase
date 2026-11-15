namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>
/// El worker de la cola de emisión (<c>Arca:Invoicing:Worker</c>). La reserva de una secuencia tiene que cubrir una vuelta que
/// concilia (<see cref="CoversARound"/>): si vence antes, el resultado de ARCA no se guarda y otro host reconcilia lo que quedó.
/// </summary>
internal sealed class ArcaVoucherWorkerOptions
{
    /// <summary>Lo que hace una vuelta que concilia, en pedidos a ARCA: el ticket, dos consultas, el último autorizado y el CAE.</summary>
    private const int RequestsInARound = 5;

    public bool ProcessInBackground { get; init; } = true;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(3);
    public int BatchSize { get; init; } = 20;

    public bool IsValid() => PollInterval > TimeSpan.Zero && Lease > TimeSpan.Zero && BatchSize is > 0 and <= 500;

    /// <summary>
    /// H23: la reserva cubre la espera del ticket que pide otro host (<c>Arca:TicketLease</c>) y los cinco pedidos de una vuelta
    /// que concilia, cada uno con su plazo (<c>Arca:RequestTimeout</c>). Los valores de fábrica dan 2 minutos y 40 segundos.
    /// </summary>
    public bool CoversARound(ArcaOptions arca) => Lease >= arca.TicketLease + arca.RequestTimeout * RequestsInARound;
}
