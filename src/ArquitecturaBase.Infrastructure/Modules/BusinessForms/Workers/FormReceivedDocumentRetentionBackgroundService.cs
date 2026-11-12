using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;

// The inbox's retention (task 7): each hour, in a scope of its own, the documents whose deletion date passed go, a batch at a time
// (IFormReceivedDocumentRetentionService); a failure is logged and the next round tries again.
internal sealed partial class FormReceivedDocumentRetentionBackgroundService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<FormReceivedDocumentRetentionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IFormReceivedDocumentRetentionService>().PurgeExpiredAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { RetentionFailed(logger, exception); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Received documents retention failed.")]
    private static partial void RetentionFailed(ILogger logger, Exception exception);
}
