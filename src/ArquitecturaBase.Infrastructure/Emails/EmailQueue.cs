using System.Threading.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Canal acotado entre los casos de uso y EmailBackgroundService, como WhatsAppSendQueue. Encolar nunca espera: el pedido
/// de código encola dentro de la transacción del caso de uso (IUnitOfWork.ExecuteInTransactionAsync), antes del commit y
/// con el lock del destino tomado, y con el SMTP caído o lento dejaría tomadas sus conexiones. Si el commit falla, el
/// email sale igual, con un código que no sirve. Con la cola llena (<c>Email:QueueCapacity</c>), el email no entra y quien
/// llama se entera por el <c>false</c>: el código queda sin marcar como enviado y la invitación, como no enviada.
/// </summary>
internal sealed partial class EmailQueue : IEmailQueue
{
    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<EmailQueue> _logger;
    private readonly int _capacity;

    public EmailQueue(IOptions<EmailOptions> options, ILogger<EmailQueue> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _capacity = options.Value.QueueCapacity;

        // Con Wait, TryWrite devuelve false si la cola está llena (DropWrite descartaría sin avisar).
        _channel = Channel.CreateBounded<EmailMessage>(
            new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    }

    public bool TryEnqueue(EmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_channel.Writer.TryWrite(message))
        {
            return true;
        }

        LogQueueFull(_logger, _capacity);

        return false;
    }

    public IAsyncEnumerable<EmailMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    // Sin el destinatario ni el contenido, que lleva el código.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The email queue is full (capacity {Capacity}); the email was dropped")]
    private static partial void LogQueueFull(ILogger logger, int capacity);
}
