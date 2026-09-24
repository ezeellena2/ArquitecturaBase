using System.Collections.Concurrent;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Emails;
using ArquitecturaBase.Infrastructure.Emails;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Emails;

public sealed class EmailBackgroundServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Failed_sends_are_retried_until_they_succeed()
    {
        var sender = new FlakyEmailSender(failuresPerMessage: 2);

        await RunAsync(sender, async queue =>
        {
            await queue.EnqueueAsync(Message("ana@example.com"), Ct);
            await WaitUntilAsync(() => sender.Sent.Count == 1);
        });

        Assert.Equal(3, sender.AttemptsFor("ana@example.com"));
    }

    [Fact]
    public async Task After_three_failed_attempts_the_email_is_dropped_and_the_next_one_is_sent()
    {
        var sender = new FlakyEmailSender(failuresPerMessage: 0, alwaysFailFor: "down@example.com");

        await RunAsync(sender, async queue =>
        {
            await queue.EnqueueAsync(Message("down@example.com"), Ct);
            await queue.EnqueueAsync(Message("ana@example.com"), Ct);
            await WaitUntilAsync(() => sender.Sent.Count == 1);
        });

        Assert.Equal(EmailBackgroundService.MaxAttempts, sender.AttemptsFor("down@example.com"));
        Assert.Equal("ana@example.com", Assert.Single(sender.Sent).To);
    }

    private static async Task RunAsync(IEmailSender sender, Func<EmailQueue, Task> act)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sender);
        await using var provider = services.BuildServiceProvider();

        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        using var service = new EmailBackgroundService(
            queue,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new EmailOptions { RetryDelaySeconds = 0 }),
            TimeProvider.System,
            NullLogger<EmailBackgroundService>.Instance);

        await service.StartAsync(Ct);

        try
        {
            await act(queue);
        }
        finally
        {
            await service.StopAsync(Ct);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private static EmailMessage Message(string to) => new(to, "Asunto", "<p>Hola</p>", "Hola");

    private sealed class FlakyEmailSender(int failuresPerMessage, string? alwaysFailFor = null) : IEmailSender
    {
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public ConcurrentQueue<EmailMessage> Sent => _sent;

        public int AttemptsFor(string to) => _attempts.GetValueOrDefault(to);

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            var attempt = _attempts.AddOrUpdate(message.To, 1, (_, previous) => previous + 1);

            if (message.To == alwaysFailFor || attempt <= failuresPerMessage)
            {
                throw new InvalidOperationException("The SMTP server is down.");
            }

            _sent.Enqueue(message);

            return Task.CompletedTask;
        }
    }
}
