using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// F40: the purge of the inbox runs one round as the worker starts, and then one each hour; a round that fails does not stop the next.
public sealed class FormReceivedDocumentRetentionBackgroundServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_worker_purges_once_at_start_and_again_every_hour_even_after_a_failed_round()
    {
        var purge = new CountingPurge();
        await using var provider = new ServiceCollection().AddSingleton(purge).AddScoped<IFormReceivedDocumentRetentionService>(services => services.GetRequiredService<CountingPurge>())
            .BuildServiceProvider();
        var clock = new FakeTimeProvider();
        var logger = new FakeLogger<FormReceivedDocumentRetentionBackgroundService>();
        using var worker = new FormReceivedDocumentRetentionBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(), clock, logger);

        await worker.StartAsync(Ct);
        try
        {
            await purge.Round(1).WaitAsync(TimeSpan.FromSeconds(10), Ct);
            // The first round came with the start: no hour passed yet.
            Assert.Equal(1, purge.Calls);
            clock.Advance(TimeSpan.FromHours(1));
            await purge.Round(2).WaitAsync(TimeSpan.FromSeconds(10), Ct);
            clock.Advance(TimeSpan.FromHours(1));
            await purge.Round(3).WaitAsync(TimeSpan.FromSeconds(10), Ct);
            // The second round threw: it was logged without its content and the third still ran.
            Assert.Single(logger.Collector.GetSnapshot());
        }
        finally { await worker.StopAsync(Ct); }
    }

    private sealed class CountingPurge : IFormReceivedDocumentRetentionService
    {
        private readonly TaskCompletionSource[] rounds = [.. Enumerable.Range(0, 3).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))];
        public int Calls { get; private set; }
        public Task Round(int number) => rounds[number - 1].Task;
        public Task<Result> PurgeExpiredAsync(CancellationToken cancellationToken)
        {
            var number = ++Calls;
            if (number <= rounds.Length) rounds[number - 1].TrySetResult();
            if (number == 2) throw new InvalidOperationException("private-purge-data");
            return Task.FromResult(Result.Success());
        }
    }
}
