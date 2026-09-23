using System.Threading.Channels;
using ArquitecturaBase.Application.Abstractions.WhatsApp;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// El aviso en memoria entre el webhook y <see cref="WhatsAppInboundProcessor"/>: un canal de un lugar que descarta lo
/// que no entra. Muchos avisos mientras el procesador trabaja lo despiertan una sola vez más, que alcanza, porque cada
/// vuelta revisa la tabla entera.
/// </summary>
internal sealed class WhatsAppInboundSignal : IWhatsAppInboundSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public void Notify() => _channel.Writer.TryWrite(true);

    /// <summary>
    /// Espera un aviso, como mucho <paramref name="timeout"/> medido con <paramref name="timeProvider"/>. Consume el
    /// aviso que encuentre, así el siguiente vuelve a esperar. Solo lanza si se cancela <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        try
        {
            await _channel.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Pasó el tiempo sin avisos: toca la revisión periódica.
        }
    }
}
