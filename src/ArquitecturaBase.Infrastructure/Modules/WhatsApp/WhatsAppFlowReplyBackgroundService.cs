using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp;

internal sealed partial class WhatsAppFlowReplyBackgroundService(IServiceScopeFactory scopeFactory,
    IOptions<WhatsAppOptions> options, TimeProvider timeProvider, ILogger<WhatsAppFlowReplyBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.ProcessFlowsInBackground) return;
        var interval = TimeSpan.FromSeconds(settings.InboundPollSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IWhatsAppFlowReplyService>().ProcessPendingAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                LogRoundFailed(logger, exception);
            }
            await Task.Delay(interval, timeProvider, stoppingToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The WhatsApp Flow reply processor round failed; pending replies will be retried")]
    private static partial void LogRoundFailed(ILogger logger, Exception exception);
}
