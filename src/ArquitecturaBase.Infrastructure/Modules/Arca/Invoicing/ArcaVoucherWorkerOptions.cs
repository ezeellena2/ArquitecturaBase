namespace ArquitecturaBase.Infrastructure.Modules.Arca.Invoicing;

/// <summary>
/// El worker de la cola de emisión (<c>Arca:Invoicing:Worker</c>). La reserva de una secuencia tiene que durar más que dos
/// pedidos a ARCA seguidos con su plazo (<c>Arca:RequestTimeout</c>); si vence, otro host reconcilia lo que quedó.
/// </summary>
internal sealed class ArcaVoucherWorkerOptions
{
    public bool ProcessInBackground { get; init; } = true;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(2);
    public int BatchSize { get; init; } = 20;

    public bool IsValid() => PollInterval > TimeSpan.Zero && Lease > TimeSpan.Zero && BatchSize is > 0 and <= 500;
}
