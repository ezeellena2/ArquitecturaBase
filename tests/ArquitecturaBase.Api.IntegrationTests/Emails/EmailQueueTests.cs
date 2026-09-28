using System.Globalization;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailQueueTests
{
    private const int Capacity = 3;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Encolar nunca espera: con la cola llena, el correo no entra, quien llama se entera y queda un log.</summary>
    [Fact]
    public async Task A_full_queue_rejects_the_email_without_waiting_and_logs_it()
    {
        var logger = new FakeLogger<EmailQueue>();
        var queue = new EmailQueue(Options.Create(new EmailOptions { QueueCapacity = Capacity }), logger);

        // Sin nadie que lea: el correo que sobra no puede dejar esperando al pedido de código.
        var accepted = Enumerable.Range(1, Capacity + 1).Select(number => queue.TryEnqueue(Message(number))).ToList();

        Assert.Equal([true, true, true, false], accepted);
        Assert.Equal(Capacity, await CountQueuedAsync(queue));
        var record = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.DoesNotContain("@example.com", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_capacity_is_100()
    {
        Assert.Equal(100, new EmailOptions().QueueCapacity);
    }

    // Lee hasta que la cola queda vacía un rato: ReadAllAsync no termina solo, porque nadie completa el canal.
    private static async Task<int> CountQueuedAsync(EmailQueue queue)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(500));
        var count = 0;

        try
        {
            await foreach (var _ in queue.ReadAllAsync(timeout.Token))
            {
                count++;
            }
        }
        catch (OperationCanceledException) when (!Ct.IsCancellationRequested)
        {
        }

        return count;
    }

    private static EmailMessage Message(int number) =>
        new(number.ToString(CultureInfo.InvariantCulture) + "@example.com", "Asunto", "<p>Hola</p>", "Hola");
}
