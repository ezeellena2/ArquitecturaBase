using System.Threading.Channels;
using ArquitecturaBase.Infrastructure.Delivery;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

/// <summary>
/// Productive instances enlist encrypted module content in the caller transaction. The two-argument
/// constructor keeps a bounded memory channel only for isolated transport tests.
/// </summary>
internal sealed partial class WhatsAppSendQueue : IWhatsAppSendQueue, IMessageSource<WhatsAppOutboundMessage>
{
    private readonly Channel<WhatsAppOutboundMessage> _channel;
    private readonly ILogger<WhatsAppSendQueue> _logger;
    private readonly int _capacity;
    private readonly DurableMessageStore? _store;

    public WhatsAppSendQueue(IOptions<WhatsAppOptions> options, ILogger<WhatsAppSendQueue> logger, DurableMessageStore store) : this(options, logger)
    {
        _store = store;
    }

    public WhatsAppSendQueue(IOptions<WhatsAppOptions> options, ILogger<WhatsAppSendQueue> logger)
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

        if (_store is not null)
        {
            return _store.TryEnqueue("whatsapp-access", _capacity, message.GetType().Name, WhatsAppQueuedMessageCodec.Serialize(message), message.ExpiresAtUtc);
        }

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
