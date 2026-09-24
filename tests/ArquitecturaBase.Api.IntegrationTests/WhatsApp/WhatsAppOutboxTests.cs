using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Emails;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

public sealed class WhatsAppOutboxTests
{
    private static readonly PhoneNumber To = PhoneNumber.Create("+5493411234567").Value;

    /// <summary>Encolar nunca espera: con la cola llena, el mensaje no entra y quien llama se entera.</summary>
    [Fact]
    public void A_full_queue_rejects_the_message_without_waiting_and_logs_it()
    {
        var logger = new FakeLogger<WhatsAppOutbox>();
        var outbox = new WhatsAppOutbox(Options.Create(new WhatsAppOptions { QueueCapacity = 2 }), logger);

        var accepted = Enumerable.Range(1, 3).Select(_ => outbox.TryEnqueue(new WhatsAppTextMessage(To, "Hola"))).ToList();

        Assert.Equal([true, true, false], accepted);
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.DoesNotContain(To.Value, record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_capacity_is_the_same_as_the_email_queue()
    {
        Assert.Equal(EmailQueue.Capacity, new WhatsAppOptions().QueueCapacity);
    }
}
