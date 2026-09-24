using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// IWhatsAppOutbox de los tests: guarda los mensajes en memoria en lugar de encolarlos para Meta, igual que
/// <see cref="CapturingEmailSender"/> con los emails. Encolar es sincrónico, así que el mensaje ya está al volver el
/// pedido que lo encoló.
/// </summary>
public sealed class CapturingWhatsAppOutbox : IWhatsAppOutbox
{
    private readonly ConcurrentQueue<WhatsAppOutboundMessage> _messages = new();

    public IReadOnlyList<WhatsAppOutboundMessage> Messages => [.. _messages];

    /// <summary>El código de un mensaje con el código de ingreso.</summary>
    public static string CodeOf(WhatsAppOutboundMessage message) =>
        Assert.IsType<WhatsAppLoginCodeMessage>(message).Code;

    public bool TryEnqueue(WhatsAppOutboundMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _messages.Enqueue(message);

        return true;
    }

    public IReadOnlyList<WhatsAppOutboundMessage> SentTo(PhoneNumber to) =>
        [.. _messages.Where(message => message.To == to)];

    public int CountFor(PhoneNumber to) => SentTo(to).Count;
}
