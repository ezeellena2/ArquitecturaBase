using System.Threading.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>Independent coalescing notices for inbound processing and Flow capture; each worker still polls durable state.</summary>
internal sealed class WhatsAppInboundSignal : IWhatsAppInboundSignal, IWhatsAppFlowReplySignal
{
    private readonly Channel<bool> _inbound = CreateNotice();
    private readonly Channel<bool> _flowReplies = CreateNotice();

    public void Notify() => _inbound.Writer.TryWrite(true);
    void IWhatsAppFlowReplySignal.Notify() => _flowReplies.Writer.TryWrite(true);

    public Task WaitAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        WaitAsync(_inbound.Reader, timeout, timeProvider, cancellationToken);

    public Task WaitForFlowReplyAsync(TimeSpan timeout, TimeProvider timeProvider, CancellationToken cancellationToken) =>
        WaitAsync(_flowReplies.Reader, timeout, timeProvider, cancellationToken);

    private static Channel<bool> CreateNotice() => Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private static async Task WaitAsync(ChannelReader<bool> reader, TimeSpan timeout, TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            await reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The timeout triggers another durable scan.
        }
    }
}
