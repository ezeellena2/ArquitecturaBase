using ArquitecturaBase.Application.Interfaces.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Infrastructure.Files;
internal sealed partial class PrivateFileCleanupBackgroundService(IServiceScopeFactory scopeFactory, TimeProvider timeProvider,
    ILogger<PrivateFileCleanupBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IPrivateFileCleanupService>().PurgeAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { CleanupFailed(logger, exception.GetType().Name); }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    [LoggerMessage(Level = LogLevel.Error, Message = "Private file cleanup failed with {ExceptionType}.")]
    private static partial void CleanupFailed(ILogger logger, string exceptionType);
}
