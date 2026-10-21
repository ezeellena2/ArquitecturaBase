using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppFlowReplyWorkerFixture : IAsyncDisposable, IWhatsAppFlowReplyService
{
    private readonly ServiceProvider _provider;
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    public TaskCompletionSource SecondRound { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ClockProbe Clock { get; } = new();
    public WhatsAppInboundSignal Signal { get; } = new();
    public WhatsAppFlowReplyBackgroundService Worker { get; }

    public WhatsAppFlowReplyWorkerFixture(bool enabled = true)
    {
        _provider = new ServiceCollection().AddSingleton<IWhatsAppFlowReplyService>(this).BuildServiceProvider();
        Worker = new(_provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new WhatsAppOptions { ProcessFlowsInBackground = enabled, InboundPollSeconds = 30 }),
            Clock, Signal, NullLogger<WhatsAppFlowReplyBackgroundService>.Instance);
    }

    public Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _calls) == 2) SecondRound.TrySetResult();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Worker.StopAsync(CancellationToken.None);
        Worker.Dispose();
        await _provider.DisposeAsync();
    }

    internal sealed class ClockProbe : FakeTimeProvider
    {
        public TaskCompletionSource Waiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = base.CreateTimer(callback, state, dueTime, period);
            if (dueTime == TimeSpan.FromSeconds(30)) Waiting.TrySetResult();
            return timer;
        }
    }
}
