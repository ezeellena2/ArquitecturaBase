using System.Threading.Channels;
using System.Text.Json;
using ArquitecturaBase.Infrastructure.Delivery;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Models.Emails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// Productive instances enlist encrypted email content in the caller transaction. The two-argument
/// constructor keeps a bounded memory channel only for isolated transport tests.
/// </summary>
internal sealed partial class EmailQueue : IEmailQueue, IMessageSource<EmailMessage>
{
    private readonly Channel<EmailMessage> _channel;
    private readonly ILogger<EmailQueue> _logger;
    private readonly int _capacity;
    private readonly DurableMessageStore? _store;

    public EmailQueue(IOptions<EmailOptions> options, ILogger<EmailQueue> logger, DurableMessageStore store) : this(options, logger)
    {
        _store = store;
    }

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

        if (_store is not null)
        {
            return _store.TryEnqueue("email", _capacity, nameof(EmailMessage), JsonSerializer.Serialize(message), message.ExpiresAtUtc);
        }

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
