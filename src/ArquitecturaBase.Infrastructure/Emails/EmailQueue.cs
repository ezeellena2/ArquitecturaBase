using System.Threading.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Emails;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Canal acotado entre los casos de uso y EmailBackgroundService. Encolar nunca espera: el pedido de código encola
/// dentro de la transacción del lock por email, y con el SMTP caído o lento dejaría tomadas sus conexiones. Con la cola
/// llena, el email se descarta (sección 8: si el email no llega, el usuario puede pedir otro código).
/// </summary>
internal sealed partial class EmailQueue(ILogger<EmailQueue> logger) : IEmailQueue
{
    public const int Capacity = 100;

    // Con Wait, TryWrite devuelve false si la cola está llena (DropWrite descartaría sin avisar).
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ValueTask EnqueueAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (!_channel.Writer.TryWrite(message))
        {
            LogQueueFull(logger, Capacity);
        }

        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    // Sin el destinatario ni el contenido, que lleva el código.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The email queue is full (capacity {Capacity}); the email was dropped")]
    private static partial void LogQueueFull(ILogger logger, int capacity);
}
