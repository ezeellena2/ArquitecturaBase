using System.Threading.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.WhatsApp;

/// <summary>
/// Canal acotado entre los casos de uso y WhatsAppSenderBackgroundService, como EmailQueue. Encolar nunca espera: el
/// pedido de código encola dentro de la transacción del lock por destino, y con Meta lenta dejaría tomadas sus
/// conexiones. Con la cola llena, el mensaje no entra y quien llama se entera por el <c>false</c>.
/// </summary>
internal sealed partial class WhatsAppOutbox : IWhatsAppOutbox
{
    private readonly Channel<WhatsAppOutboundMessage> _channel;
    private readonly ILogger<WhatsAppOutbox> _logger;
    private readonly int _capacity;

    public WhatsAppOutbox(IOptions<WhatsAppOptions> options, ILogger<WhatsAppOutbox> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _capacity = options.Value.QueueCapacity;

        // Con Wait, TryWrite devuelve false si la cola está llena (DropWrite descartaría sin avisar).
        _channel = Channel.CreateBounded<WhatsAppOutboundMessage>(
            new BoundedChannelOptions(_capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    }

    public bool TryEnqueue(WhatsAppOutboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_channel.Writer.TryWrite(message))
        {
            return true;
        }

        LogQueueFull(_logger, _capacity);

        return false;
    }

    public IAsyncEnumerable<WhatsAppOutboundMessage> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    // Sin el destinatario ni el contenido, que puede llevar un código o un enlace.
    [LoggerMessage(Level = LogLevel.Warning, Message = "The WhatsApp queue is full (capacity {Capacity}); the message was not queued")]
    private static partial void LogQueueFull(ILogger logger, int capacity);
}
