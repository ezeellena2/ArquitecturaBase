using System.Threading.Channels;
using ArquitecturaBase.Application.Abstractions.Emails;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>Canal acotado entre los casos de uso y EmailBackgroundService. Si se llena, el que encola espera.</summary>
internal sealed class EmailQueue : IEmailQueue
{
    public const int Capacity = 100;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
