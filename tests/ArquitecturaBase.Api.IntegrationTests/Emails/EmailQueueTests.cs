using System.Globalization;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailQueueTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Enqueueing_never_waits_and_a_full_queue_drops_the_email()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);

        // Sin nadie que lea: el mensaje que sobra no puede dejar esperando al pedido de código.
        var enqueues = Enumerable.Range(1, EmailQueue.Capacity + 1)
            .Select(number => queue.EnqueueAsync(Message(number), Ct).AsTask())
            .ToList();

        Assert.All(enqueues, enqueue => Assert.True(enqueue.IsCompletedSuccessfully));
        Assert.Equal(EmailQueue.Capacity, await CountQueuedAsync(queue));
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
