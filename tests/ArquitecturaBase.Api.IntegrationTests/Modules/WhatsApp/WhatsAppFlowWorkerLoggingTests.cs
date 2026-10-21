using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppFlowWorkerLoggingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Flow_worker_logs_exception_type_without_exception_message()
    {
        const string secretMarker = "access_token=secret-value";
        await using var provider = new ServiceCollection()
            .AddSingleton<IWhatsAppFlowDispatchService>(new ThrowingWhatsAppFlowDispatch(secretMarker))
            .BuildServiceProvider();
        var logger = new FakeLogger<WhatsAppFlowBackgroundService>();
        var worker = new WhatsAppFlowBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WhatsAppOptions { ProcessFlowsInBackground = true }), TimeProvider.System, logger);

        await worker.StartAsync(Ct);
        await WaitForLogAsync(logger, Ct);
        await worker.StopAsync(Ct);

        var message = Assert.Single(logger.Collector.GetSnapshot()).Message;
        Assert.Contains(nameof(InvalidOperationException), message, StringComparison.Ordinal);
        Assert.DoesNotContain(secretMarker, message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Flow_reply_worker_runs_when_text_inbound_processing_is_disabled()
    {
        var processor = new CountingFlowReplyService();
        await using var provider = new ServiceCollection()
            .AddSingleton<IWhatsAppFlowReplyService>(processor)
            .BuildServiceProvider();
        var worker = new WhatsAppFlowReplyBackgroundService(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WhatsAppOptions { ProcessInboundInBackground = false, ProcessFlowsInBackground = true,
                InboundPollSeconds = 1 }), TimeProvider.System, new WhatsAppInboundSignal(), new FakeLogger<WhatsAppFlowReplyBackgroundService>());

        await worker.StartAsync(Ct);
        await processor.Called.Task.WaitAsync(Ct);
        await worker.StopAsync(Ct);

        Assert.Equal(1, processor.Calls);
    }

    private static async Task WaitForLogAsync<T>(FakeLogger<T> logger, CancellationToken cancellationToken) where T : class
    {
        for (var attempt = 0; attempt < 100 && logger.Collector.GetSnapshot().Count == 0; attempt++)
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        Assert.NotEmpty(logger.Collector.GetSnapshot());
    }

    private sealed class ThrowingWhatsAppFlowDispatch(string message) : IWhatsAppFlowDispatchService
    {
        public Task<IReadOnlyList<Guid>> ListDueOperationIdsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException(message);

        public Task<Result<WhatsAppFlowWorkResponse?>> ReserveAsync(Guid? operationId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(message);

        public Task<Result> MarkEffectStartedAsync(WhatsAppFlowWorkResponse work, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task<Result> CompleteEffectAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowEffectResponse response,
            CancellationToken cancellationToken) => throw new NotImplementedException();

        public Task<Result> CompleteVerificationAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowVerificationResponse response,
            CancellationToken cancellationToken) => throw new NotImplementedException();
    }

    private sealed class CountingFlowReplyService : IWhatsAppFlowReplyService
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ProcessPendingAsync(CancellationToken cancellationToken)
        {
            Calls++;
            Called.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
